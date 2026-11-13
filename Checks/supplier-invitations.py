"""Run locally with OFFICER_TEST_PASSWORD set. Temporary tenders are removed."""
import http.cookiejar
import os
import re
import subprocess
import urllib.error
import urllib.parse
import urllib.request
import uuid

base = 'http://localhost:5072'
prefix = 'InvitationCheck-' + uuid.uuid4().hex

def sql(query):
    run = subprocess.run(['sqlcmd', '-S', 'localhost', '-E', '-C', '-I', '-b', '-W', '-h', '-1',
        '-d', 'SourcingTenderManagementCodeFirst', '-Q', 'SET NOCOUNT ON; ' + query], capture_output=True, text=True)
    assert run.returncode == 0, run.stderr + run.stdout
    return run.stdout.strip()

client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
def token(html):
    return re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', html).group(1)

login = client.open(base + '/Account/Login').read().decode()
response = client.open(base + '/Account/Login', urllib.parse.urlencode({'Email': 'officer@tender.local',
    'Password': os.environ['OFFICER_TEST_PASSWORD'], '__RequestVerificationToken': token(login)}).encode())
assert '/SourcingOfficer/' in response.url, 'Login failed'

def send(tender, suppliers=(), all_suppliers=False):
    html = client.open(base + '/SourcingOfficer/Invitations').read().decode()
    values = [('__RequestVerificationToken', token(html)), ('tenderId', tender)]
    values += [('supplierProfileIds', supplier) for supplier in suppliers]
    if all_suppliers:
        values.append(('allSuppliers', 'true'))
    return client.open(base + '/SourcingOfficer/CreateInvitation', urllib.parse.urlencode(values).encode()).read().decode()

try:
    approved = list(map(int, sql("SELECT SupplierProfileId FROM SupplierProfiles WHERE Status='Approved' ORDER BY SupplierProfileId").split()))
    assert len(approved) >= 3, 'Use at least three approved demo suppliers.'
    tender = int(sql(f"""INSERT Tenders(PurchaseRequestId,CreatedByUserId,Title,Description,OpensAt,ClosesAt,Status)
    SELECT TOP(1) PurchaseRequestId,(SELECT Id FROM AspNetUsers WHERE Email='officer@tender.local'),N'{prefix}',
    'Temporary invitation test',GETUTCDATE(),DATEADD(day,7,GETUTCDATE()),'Published' FROM PurchaseRequests;
    SELECT CONVERT(int,SCOPE_IDENTITY());"""))
    count = lambda: int(sql(f'SELECT COUNT(*) FROM Invitations WHERE TenderId={tender}'))
    html = client.open(base + '/SourcingOfficer/Invitations').read().decode()
    assert 'name="supplierProfileIds"' in html and 'Select all suppliers' in html
    send(tender)
    assert count() == 0, 'Empty selection accepted'
    send(tender, [approved[0], -1])
    assert count() == 0, 'Invalid supplier caused partial invitations'
    send(tender, [approved[0], approved[1], approved[0]])
    assert count() == 2, 'Multiple suppliers or deduplication failed'
    send(tender, all_suppliers=True)
    assert count() == len(approved), 'Select all did not include every approved supplier'
    send(tender, all_suppliers=True)
    assert count() == len(approved), 'Duplicate invitations created'
    sql(f"UPDATE Tenders SET Status='Draft' WHERE TenderId={tender}; DELETE Invitations WHERE TenderId={tender};")
    send(tender, [approved[0]])
    assert count() == 0, 'Draft tender accepted invitations'
    print('PASS: checkbox rendering, multiple suppliers, select all, duplicate prevention, invalid/empty selections, and Published status validation.')
finally:
    sql(f"SET XACT_ABORT ON; BEGIN TRANSACTION; DELETE i FROM Invitations i JOIN Tenders t ON t.TenderId=i.TenderId WHERE t.Title=N'{prefix}'; DELETE Tenders WHERE Title=N'{prefix}'; COMMIT;")
    print('Temporary invitation fixtures removed.')
