"""Set SUPPLIER_TEST_PASSWORD and run locally. Removes its temporary records."""
import http.cookiejar, html, os, re, subprocess, urllib.error, urllib.parse, urllib.request, uuid
base = 'http://localhost:5072'
prefix = 'DeadlineCheck-' + uuid.uuid4().hex

def sql(query):
    result = subprocess.run(['sqlcmd','-S','localhost','-E','-C','-I','-b','-W','-h','-1','-d','SourcingTenderManagementCodeFirst',
        '-Q','SET NOCOUNT ON; '+query], capture_output=True, text=True)
    assert result.returncode == 0, result.stderr + result.stdout
    return result.stdout.strip()

client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
def csrf(text): return re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', text).group(1)
def request(path, values=None):
    try:
        response = client.open(base + path, None if values is None else urllib.parse.urlencode(values).encode())
        return response.status, response.url, response.read().decode()
    except urllib.error.HTTPError as error: return error.code, error.url, error.read().decode()

_, _, login = request('/Account/Login')
_, login_url, _ = request('/Account/Login', {'Email':'supplier@tender.local','Password':os.environ['SUPPLIER_TEST_PASSWORD'],'__RequestVerificationToken':csrf(login)})
assert '/Supplier/' in login_url, 'Supplier login failed'
tender = None
try:
    tender, invitation, item = map(int, sql(f"""
    SET XACT_ABORT ON; BEGIN TRANSACTION;
    DECLARE @item int=(SELECT TOP(1) PurchaseRequestItemId FROM PurchaseRequestItems ORDER BY PurchaseRequestItemId);
    INSERT Tenders(PurchaseRequestId,CreatedByUserId,Title,Description,OpensAt,ClosesAt,Status)
    SELECT PurchaseRequestId,(SELECT Id FROM AspNetUsers WHERE Email='officer@tender.local'),N'{prefix}','Temporary deadline test',DATEADD(day,1,GETUTCDATE()),DATEADD(day,2,GETUTCDATE()),'Published'
    FROM PurchaseRequestItems WHERE PurchaseRequestItemId=@item;
    DECLARE @t int=SCOPE_IDENTITY();
    INSERT Invitations(TenderId,SupplierProfileId,InvitedByUserId,Status)
    SELECT @t,SupplierProfileId,(SELECT Id FROM AspNetUsers WHERE Email='officer@tender.local'),'Accepted'
    FROM SupplierProfiles WHERE UserId=(SELECT Id FROM AspNetUsers WHERE Email='supplier@tender.local');
    DECLARE @i int=SCOPE_IDENTITY(); COMMIT; SELECT @t,@i,@item;
    """).split())
    code, _, detail = request(f'/Supplier/Invitation/{invitation}')
    assert code == 200 and 'Prepare offer' in detail and 'Offer submission opens' not in detail
    code, _, form = request(f'/Supplier/CreateOffer?invitationId={invitation}')
    assert code == 200 and 'Itemized offer' in form, 'Future opening date blocked form'
    values = {'InvitationId':invitation,'TenderId':tender,'TenderTitle':prefix,'Currency':'JOD','DeliveryDays':10,
        'Items[0].PurchaseRequestItemId':item,'Items[0].ItemName':'Test item','Items[0].Unit':'Each',
        'Items[0].RequestedSpecifications':'Test','Items[0].Specifications':'Test','Items[0].Quantity':1,'Items[0].UnitPrice':100,
        '__RequestVerificationToken':csrf(form),'submissionCommand':'Draft'}
    request('/Supplier/SaveOffer', values)
    bid = int(sql(f'SELECT BidId FROM Bids WHERE TenderId={tender}'))
    assert sql(f'SELECT Status FROM Bids WHERE BidId={bid}') == 'Draft', 'Draft before future opening failed'
    values.update({'BidId':bid,'submissionCommand':'Submit'})
    request('/Supplier/SaveOffer', values)
    assert sql(f'SELECT Status FROM Bids WHERE BidId={bid}') == 'Submitted', 'Submission before deadline failed'
    sql(f"UPDATE Bids SET Status='Draft',SubmittedAt=NULL WHERE BidId={bid}; UPDATE Tenders SET OpensAt=DATEADD(day,-2,GETUTCDATE()),ClosesAt=DATEADD(minute,-1,GETUTCDATE()) WHERE TenderId={tender};")
    for command in ['Draft','Submit']:
        values['submissionCommand'] = command
        code, url, _ = request('/Supplier/SaveOffer', values)
        assert code == 403 or '/Account/AccessDenied' in url, 'Expired offer save or submit accepted'
    code, url, _ = request(f'/Supplier/CreateOffer?invitationId={invitation}&bidId={bid}')
    assert code == 403 or '/Account/AccessDenied' in url, 'Expired draft form opened'
    _, _, detail = request(f'/Supplier/Invitation/{invitation}')
    assert 'Continue draft' not in detail and 'Submission period has ended' in detail
    assert sql(f'SELECT Status FROM Bids WHERE BidId={bid}') == 'Draft', 'Expired request changed bid'
    print('PASS: create, save and submit before deadline despite future opening; draft and submit blocked after deadline; matching invitation buttons.')
finally:
    sql(f"""SET XACT_ABORT ON; BEGIN TRANSACTION;
    DELETE n FROM Notifications n WHERE n.RelativeUrl='/SourcingOfficer/Offers?tenderId={tender}' AND n.Message LIKE N'%{prefix}%';
    DELETE bi FROM BidItems bi JOIN Bids b ON b.BidId=bi.BidId JOIN Tenders t ON t.TenderId=b.TenderId WHERE t.Title=N'{prefix}';
    DELETE b FROM Bids b JOIN Tenders t ON t.TenderId=b.TenderId WHERE t.Title=N'{prefix}';
    DELETE i FROM Invitations i JOIN Tenders t ON t.TenderId=i.TenderId WHERE t.Title=N'{prefix}';
    DELETE Tenders WHERE Title=N'{prefix}'; COMMIT;""")
    print('Temporary deadline fixtures removed.')
