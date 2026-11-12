from pathlib import Path
import subprocess, os, uuid, json, re, urllib.request, urllib.parse, urllib.error, http.cookiejar, html
from html.parser import HTMLParser

class SidebarLinks(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links=[]
    def handle_starttag(self, tag, attrs):
        attrs=dict(attrs)
        if tag=='a' and 'page-link-item' in attrs.get('class','').split():
            self.links.append(attrs.get('href'))

root = Path(__file__).resolve().parents[1]
base = os.environ.get('TENDER_TEST_BASE_URL', 'http://localhost:5072')
tag = 'check-' + uuid.uuid4().hex[:12]
password = 'Check!' + uuid.uuid4().hex + 'A1'
admin_email = tag + '-admin@example.invalid'
def sql(query):
    p = subprocess.run(['sqlcmd','-S','localhost','-E','-C','-I','-d','SourcingTenderManagementCodeFirst','-b','-h','-1','-W','-Q','SET NOCOUNT ON; '+query], capture_output=True, text=True)
    if p.returncode: raise RuntimeError(p.stdout+p.stderr)
    return p.stdout.strip()
def session(): return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
def request(client, path, data=None):
    payload=urllib.parse.urlencode(data).encode() if data is not None else None
    try:
        with client.open(base+path, data=payload) as response:
            return response.status, response.url, response.read().decode()
    except urllib.error.HTTPError as error:
        return error.code, error.url, error.read().decode()
def token(client,path):
    status,_,text=request(client,path)
    assert status==200, (path,status)
    match=re.search(r'<input[^>]*name="__RequestVerificationToken"[^>]*value="([^"]+)"',text)
    assert match, ('Missing anti-forgery token',path)
    return html.unescape(match[1])
def post(client,path,data,form_path=None):
    return request(client,path,dict(data,__RequestVerificationToken=token(client,form_path or path)))
def login(email):
    client=session()
    status,url,text=post(client,'/Account/Login',{'Email':email,'Password':password})
    assert status==200 and '/Account/Login' not in url, (email,status,url)
    return client,url

def failed_login(email, supplied_password, expected):
    status,url,text=post(session(),'/Account/Login',{'Email':email,'Password':supplied_password})
    assert status==200 and '/Account/Login' in url and expected in text, ('Login validation',status,url)
    if supplied_password:
        assert supplied_password not in text, 'Password was rendered into the response.'

try:
    env=dict(os.environ,TENDER_ADMIN_PASSWORD=password)
    result=subprocess.run(['dotnet','run','--no-build','--no-launch-profile','--','--create-admin',admin_email],cwd=root,env=env,capture_output=True,text=True)
    assert result.returncode==0, result.stdout+result.stderr
    admin,url=login(admin_email)
    role_clients={'Admin':admin}
    assert url.endswith('/Admin/Dashboard'),url
    failed_login('', '', 'Enter your email address.')
    failed_login('not-an-email', password, 'Enter a valid email address.')
    failed_login(admin_email, 'Wrong!Password1', 'Unable to sign in.')
    failed_login(tag+'-missing@example.invalid', password, 'Unable to sign in.')
    assert request(admin,'/Account/Login')[1].endswith('/Admin/Dashboard')
    assert request(session(),'/Admin/Users')[1].find('/Account/Login')>=0
    assert request(admin,'/Admin/CreateDepartment',{'name':'CSRF must fail'})[0]==400
    status,url,_=post(admin,'/Admin/CreateDepartment',{'name':tag},'/Admin/Departments')
    assert status==200 and url.endswith('/Admin/Departments'),(status,url)
    department_id=sql(f"SELECT DepartmentId FROM Departments WHERE Name=N'{tag}';")
    status,url,_=post(admin,'/Admin/EditDepartment',{'DepartmentId':department_id,'Name':tag+'-edited','IsActive':'true'},'/Admin/Departments')
    assert status==200 and url.endswith('/Admin/Departments')
    assert sql(f'SELECT Name FROM Departments WHERE DepartmentId={department_id};')==tag+'-edited'
    post(admin,'/Admin/EditDepartment',{'DepartmentId':department_id,'Name':tag,'IsActive':'true'},'/Admin/Departments')
    status,_,text=post(admin,'/Admin/CreateUser',{'FullName':'','Email':tag+'-invalid@example.invalid','Password':password,'Role':'SourcingOfficer'},'/Admin/Users')
    assert status==200 and 'id="create-user-modal"' in text and 'bootstrap.Modal.getOrCreateInstance' in text
    assert sql(f"SELECT COUNT(*) FROM AspNetUsers WHERE Email=N'{tag}-invalid@example.invalid';")=='0'
    for removed in ['Reports','ReportDetails','Clarifications','NewClarification','ClarificationDetails','Notifications']:
        assert request(admin,'/Admin/'+removed)[0]==404,removed
    assert request(admin,'/Admin/CreateUser')[0]==405
    assert 'id="notification-bell"' not in request(session(),'/')[2]
    routes=json.loads((root/'Checks/routes.json').read_text())
    count=0
    for role,actions in routes.items():
        if role=='Supplier': continue
        if role=='Admin': client=admin
        else:
            email=tag+'-'+role.lower()+'@example.invalid'
            data={'FullName':'Temporary '+role,'Email':email,'Password':password,'Role':role,'DepartmentId':department_id if role=='Department' else ''}
            if role=='EvaluationCommittee': data['CommitteeRole']='EvaluationTechnical'
            if role=='TopCommittee': data['CommitteeRole']='TopCommitteeMember1'
            status,url,text=post(admin,'/Admin/CreateUser',data,'/Admin/Users')
            assert status==200 and url.endswith('/Admin/Users'),(role,status,url,text[:1000])
            client,url=login(email)
            role_clients[role]=client
            assert url.endswith('/'+role+'/Dashboard'),(role,url)
            assert request(client,'/Admin/Users')[0]==403,role
            assert request(client,'/Account/Login')[1].endswith('/'+role+'/Dashboard'),role
        for action in actions:
            status,url,text=request(client,f'/{role}/{action}')
            assert status==200 and f'/{role}/{action}' in url,(role,action,status,url)
            assert 'id="notification-bell"' in text,(role,'Missing navbar bell')
            sidebar=re.search(r'<aside\b[^>]*>(.*?)</aside>',text,re.S)
            assert sidebar,(role,'Missing sidebar')
            parser=SidebarLinks()
            parser.feed(sidebar[1])
            assert parser.links==['/'+role+'/'+a for a in actions],(role,'Sidebar differs from merged page map',parser.links)
            assert not re.search(r'href="/'+role+r'/Notifications(?:["/?])',text),(role,'Standalone notification link')
            if (role,action) in [('EvaluationCommittee','Evaluations'),('TopCommittee','Decisions')]:
                assert text.count('Your form')==1 and text.count('Read-only')==3,(role,'Workspace ownership')
            if (role,action)==('SourcingManager','Submissions'):
                assert 'Manager Summary for the Top Committee' in text and 'Reject Recommendation' in text
            if (role,action)==('SourcingManager','ChosenOffers'):
                assert 'Approve Report' in text and '>Edit</button>' not in text
            count+=1
        assert request(client,'/'+role)[1].endswith('/'+role+'/Dashboard'),role
    department_client=role_clients['Department']
    for removed in ['Reports','ReportDetails','CreateRequest','EditRequest','RequestDetails','NewClarification','ClarificationDetails','Notifications']:
        assert request(department_client,'/Department/'+removed)[0]==404,removed
    assert request(role_clients['SourcingOfficer'],'/SourcingOfficer/Award')[0]==404
    admin_id=sql(f"SELECT Id FROM AspNetUsers WHERE Email=N'{admin_email}';")
    officer_id=sql(f"SELECT Id FROM AspNetUsers WHERE Email=N'{tag}-sourcingofficer@example.invalid';")
    sql(f"INSERT Notifications(UserId,Title,Message,RelativeUrl,IsRead) VALUES(N'{admin_id}',N'{tag}-admin',N'Private admin notification',N'/Admin/Users',0),(N'{officer_id}',N'{tag}-officer',N'Private officer notification',N'/SourcingOfficer/Tenders',0);")
    admin_html=request(admin,'/Admin/Dashboard')[2]
    officer_html=request(role_clients['SourcingOfficer'],'/SourcingOfficer/Dashboard')[2]
    assert tag+'-admin' in admin_html and tag+'-officer' not in admin_html
    assert tag+'-officer' in officer_html and tag+'-admin' not in officer_html
    assert 'Notifications, 1 unread' in admin_html
    officer_email=tag+'-sourcingofficer@example.invalid'
    for _ in range(5): failed_login(officer_email, 'Wrong!Password1', 'Unable to sign in.')
    failed_login(officer_email, password, 'Unable to sign in.')
    assert sql(f"SELECT CASE WHEN LockoutEnd > SYSDATETIMEOFFSET() THEN 1 ELSE 0 END FROM AspNetUsers WHERE Email=N'{officer_email}';")=='1'
    department_email=tag+'-department@example.invalid'
    department_client,_=login(department_email)
    department_user_id=sql(f"SELECT Id FROM AspNetUsers WHERE Email=N'{department_email}';")
    status,url,_=post(admin,'/Admin/EditUser',{'Id':department_user_id,'FullName':'Edited Department Account','DepartmentId':department_id,'IsActive':'false'},'/Admin/Users')
    assert status==200 and url.endswith('/Admin/Users')
    assert sql(f"SELECT FullName FROM AspNetUsers WHERE Id=N'{department_user_id}';")=='Edited Department Account'
    failed_login(department_email,password,'Unable to sign in.')
    assert '/Account/Login' in request(department_client,'/Department/Dashboard')[1]
    supplier=session()
    supplier_email=tag+'-supplier@example.invalid'
    data={'CompanyName':'Temporary supplier','RegistrationNumber':tag,'ContactName':'Verification Contact','Email':supplier_email,'Password':password}
    status,url,text=post(supplier,'/Account/Register',data)
    assert status==200 and url.endswith('/Account/RegistrationStatus'),(status,url,text[:1000])
    assert 'Pending' in text
    assert request(supplier,'/Supplier/Dashboard')[0]==403
    supplier_id=sql(f"SELECT SupplierProfileId FROM SupplierProfiles WHERE RegistrationNumber=N'{tag}';")
    status,_,_=post(admin,'/Admin/ReviewSupplier',{'id':supplier_id,'status':'ChangesRequested','notes':'Please update the company name.'},'/Admin/SupplierApplications')
    assert status==200
    status,_,_=post(supplier,'/Account/EditRegistration',{'CompanyName':'Corrected supplier','RegistrationNumber':tag,'ContactName':'Verification Contact'})
    assert status==200
    assert sql(f'SELECT Status FROM SupplierProfiles WHERE SupplierProfileId={supplier_id};')=='Pending'
    post(admin,'/Admin/ReviewSupplier',{'id':supplier_id,'status':'Approved'},'/Admin/SupplierApplications')
    admin_suppliers=request(admin,'/Admin/Suppliers')[2]
    assert '<details' in admin_suppliers and 'Corrected supplier' in admin_suppliers and supplier_email in admin_suppliers
    fixture_ids=sql(f"""
    DECLARE @request int, @item int, @tender int, @otherTender int, @invitation int, @bid int;
    INSERT PurchaseRequests(DepartmentId,RequestedByUserId,Title,Reason,Priority,TargetDate)
    VALUES({department_id},N'{officer_id}',N'{tag}-request',N'Temporary page verification','Normal','2030-01-01');
    SET @request=SCOPE_IDENTITY();
    INSERT PurchaseRequestItems(PurchaseRequestId,ItemName,Quantity,Unit,Specifications) VALUES(@request,N'Test item',2,N'Each',N'Test');
    SET @item=SCOPE_IDENTITY();
    INSERT Tenders(PurchaseRequestId,CreatedByUserId,Title,Description,OpensAt,ClosesAt)
    VALUES(@request,N'{officer_id}',N'{tag}-owned',N'Temporary page verification','2030-01-01','2030-02-01');
    SET @tender=SCOPE_IDENTITY();
    INSERT Tenders(PurchaseRequestId,CreatedByUserId,Title,Description,OpensAt,ClosesAt)
    VALUES(@request,N'{admin_id}',N'{tag}-other-owner',N'Temporary page verification','2030-01-01','2030-02-01');
    SET @otherTender=SCOPE_IDENTITY();
    INSERT Invitations(TenderId,SupplierProfileId,InvitedByUserId) VALUES(@tender,{supplier_id},N'{officer_id}');
    SET @invitation=SCOPE_IDENTITY();
    INSERT Bids(InvitationId,TenderId,Currency,DeliveryDays,TaxAmount,AdditionalCosts) VALUES(@invitation,@tender,'USD',10,10,5);
    SET @bid=SCOPE_IDENTITY();
    INSERT BidItems(BidId,PurchaseRequestItemId,Quantity,UnitPrice,OfferedSpecifications) VALUES(@bid,@item,2,100,N'Test');
    UPDATE Tenders SET Status='Awarded',AwardedBidId=@bid,AwardedAt=SYSUTCDATETIME(),AwardNotes=N'{tag}-decision' WHERE TenderId=@tender;
    SELECT CONCAT(@tender,'|',@otherTender);
    """)
    awarded_tender_id,foreign_tender_id=fixture_ids.split('|')
    status,_,awarded_html=request(role_clients['SourcingOfficer'],'/SourcingOfficer/Tenders/'+awarded_tender_id)
    assert status==200 and 'Corrected supplier' in awarded_html and tag+'-decision' in awarded_html and '215.00 USD' in awarded_html
    assert tag+'-other-owner' not in awarded_html
    assert request(role_clients['SourcingOfficer'],'/SourcingOfficer/Tenders/'+foreign_tender_id)[0]==404
    for action in routes['Supplier']:
        status,url,_=request(supplier,'/Supplier/'+action)
        assert status==200,('Supplier',action,status,url)
        count+=1
    assert request(supplier,'/Supplier/Reports')[0]==404
    history_html=request(supplier,'/Supplier/History')[2]
    assert 'Submitted Offers' in history_html and 'Offer Summary' in history_html and 'Download My Summary' in history_html
    assert 'My Reports' not in history_html and '<h2>Report Details</h2>' not in history_html
    assert request(supplier,'/Admin/Users')[0]==403
    assert post(supplier,'/Account/Logout',{},'/Account/RegistrationStatus')[0]==200
    print(f'PASS: {count} merged role destinations; seven role logins; user-popup validation; inline user/department edits; removed routes; private navbar notifications; supplier details; login validation/lockout; role restrictions; committee ownership; supplier registration, correction, approval and sign-out.')
finally:
    # Remove only fixtures created by this invocation, identified by a random exact prefix.
    sql(f"""BEGIN TRANSACTION;
    UPDATE t SET Status='Cancelled',AwardedBidId=NULL,AwardedAt=NULL FROM Tenders t JOIN AspNetUsers u ON u.Id=t.CreatedByUserId WHERE u.Email LIKE N'{tag}-%@example.invalid';
    DELETE bi FROM BidItems bi JOIN Bids b ON b.BidId=bi.BidId JOIN Invitations i ON i.InvitationId=b.InvitationId JOIN SupplierProfiles s ON s.SupplierProfileId=i.SupplierProfileId WHERE s.RegistrationNumber=N'{tag}';
    DELETE b FROM Bids b JOIN Invitations i ON i.InvitationId=b.InvitationId JOIN SupplierProfiles s ON s.SupplierProfileId=i.SupplierProfileId WHERE s.RegistrationNumber=N'{tag}';
    DELETE i FROM Invitations i JOIN SupplierProfiles s ON s.SupplierProfileId=i.SupplierProfileId WHERE s.RegistrationNumber=N'{tag}';
    DELETE t FROM Tenders t JOIN AspNetUsers u ON u.Id=t.CreatedByUserId WHERE u.Email LIKE N'{tag}-%@example.invalid';
    DELETE i FROM PurchaseRequestItems i JOIN PurchaseRequests r ON r.PurchaseRequestId=i.PurchaseRequestId JOIN AspNetUsers u ON u.Id=r.RequestedByUserId WHERE u.Email LIKE N'{tag}-%@example.invalid';
    DELETE r FROM PurchaseRequests r JOIN AspNetUsers u ON u.Id=r.RequestedByUserId WHERE u.Email LIKE N'{tag}-%@example.invalid';
    DELETE n FROM Notifications n JOIN AspNetUsers u ON u.Id=n.UserId WHERE u.Email LIKE N'{tag}-%@example.invalid';
    DELETE FROM SupplierProfiles WHERE RegistrationNumber=N'{tag}';
    DELETE FROM AspNetUsers WHERE Email LIKE N'{tag}-%@example.invalid';
    DELETE FROM Departments WHERE Name=N'{tag}';
    COMMIT;""")
    print('Temporary HTTP verification data removed.')
