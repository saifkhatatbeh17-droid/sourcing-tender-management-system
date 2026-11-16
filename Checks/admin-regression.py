"""Run from the project folder with ADMIN_TEST_PASSWORD set and the local app running.
Requires the demo committee accounts. Removes its temporary account, document and file.
"""
import http.cookiejar
import os
import re
import subprocess
import urllib.parse
import urllib.request
import uuid
from pathlib import Path

base = os.environ.get('TEST_BASE_URL', 'http://localhost:5072')
prefix = 'AdminCheck-' + uuid.uuid4().hex
email = prefix.lower() + '@example.invalid'
client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

def sql(query):
    result = subprocess.run(['sqlcmd', '-S', 'localhost', '-E', '-C', '-I', '-b', '-W', '-h', '-1',
        '-d', 'SourcingTenderManagementCodeFirst', '-Q', 'SET NOCOUNT ON; ' + query], capture_output=True, text=True)
    assert result.returncode == 0, result.stderr + result.stdout
    return result.stdout.strip()

def token(body):
    return re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', body).group(1)

def post(action, values):
    body = client.open(base + '/Admin/Users').read().decode()
    response = client.open(base + '/Admin/' + action, urllib.parse.urlencode(
        dict(values, __RequestVerificationToken=token(body))).encode())
    return response.read().decode()

login = client.open(base + '/Account/Login').read().decode()
response = client.open(base + '/Account/Login', urllib.parse.urlencode({
    'Email': os.environ.get('ADMIN_TEST_EMAIL', 'admin@tender.local'),
    'Password': os.environ['ADMIN_TEST_PASSWORD'], '__RequestVerificationToken': token(login)}).encode())
assert '/Admin/' in response.url, 'Admin login failed.'

positions = ['EvaluationSourcingManager', 'EvaluationDepartment', 'EvaluationFinancial', 'EvaluationTechnical']
positions += ['TopCommitteeMember' + str(n) for n in range(1, 5)]
document = None
folder = Path('App_Data/private-files/suppliers')
folder.mkdir(parents=True, exist_ok=True)
file = folder / (prefix + '.txt')
try:
    file.write_bytes(b'Temporary admin document regression check.')
    document = int(sql(f"""INSERT SupplierDocuments(SupplierProfileId,DocumentType,OriginalFileName,
        StoredFileName,ContentType,FileSizeBytes,UploadedAt)
        SELECT TOP(1) SupplierProfileId,'Registration','admin-check.txt','{file.name}','text/plain',
        {file.stat().st_size},GETUTCDATE() FROM SupplierProfiles;
        SELECT CONVERT(int,SCOPE_IDENTITY());"""))
    download = client.open(base + '/Admin/SupplierDocument/' + str(document))
    assert download.read() == file.read_bytes(), 'Admin cannot read the registration upload folder.'
    assert 'attachment' in download.headers['Content-Disposition']
    assert download.headers['X-Content-Type-Options'] == 'nosniff'

    create = {'FullName': prefix, 'Email': email, 'Password': 'Test!' + uuid.uuid4().hex + 'A7'}
    for position in positions:
        active = sql(f"""SELECT COUNT(*) FROM AspNetUsers u JOIN AspNetUserRoles ur ON u.Id=ur.UserId
            JOIN AspNetRoles r ON r.Id=ur.RoleId WHERE u.IsActive=1 AND r.Name='{position}'""")
        assert active == '1', 'Requires one active demo account for ' + position
        role = 'TopCommittee' if position.startswith('Top') else 'EvaluationCommittee'
        body = post('CreateUser', dict(create, Role=role, CommitteeRole=position))
        assert 'already has an active account' in body, 'Duplicate position accepted: ' + position
        assert sql(f"SELECT COUNT(*) FROM AspNetUsers WHERE Email='{email}'") == '0'
    body = post('CreateUser', dict(create, Role='SourcingManager', CommitteeRole='EvaluationSourcingManager'))
    assert 'already has an active account' in body, 'Manager bypassed committee validation.'

    post('CreateUser', dict(create, Role='SourcingOfficer'))
    user = sql(f"SELECT Id FROM AspNetUsers WHERE Email='{email}'")
    assert user, 'Normal employee creation failed.'
    post('EditUser', {'Id': user, 'FullName': prefix, 'IsActive': 'false'})
    assert sql(f"SELECT CONVERT(int,IsActive) FROM AspNetUsers WHERE Id='{user}'") == '0'
    # Give only this disposable inactive fixture the positions; real member accounts stay unchanged.
    sql(f"INSERT AspNetUserRoles(UserId,RoleId) SELECT '{user}',Id FROM AspNetRoles WHERE Name IN (" +
        ','.join("'" + position + "'" for position in positions) + ')')
    body = post('EditUser', {'Id': user, 'FullName': prefix, 'IsActive': 'true'})
    assert 'already has an active account' in body, 'Duplicate account reactivated.'
    assert sql(f"SELECT CONVERT(int,IsActive) FROM AspNetUsers WHERE Id='{user}'") == '0'
    sql(f"""DELETE FROM AspNetUserRoles WHERE UserId='{user}' AND RoleId IN
        (SELECT Id FROM AspNetRoles WHERE Name<>'SourcingOfficer')""")
    post('EditUser', {'Id': user, 'FullName': prefix, 'IsActive': 'true'})
    assert sql(f"SELECT CONVERT(int,IsActive) FROM AspNetUsers WHERE Id='{user}'") == '1'
    post('EditUser', {'Id': user, 'FullName': prefix + ' Edited', 'IsActive': 'true'})
    assert sql(f"SELECT FullName FROM AspNetUsers WHERE Id='{user}'") == prefix + ' Edited'
    print('PASS: private document download; all eight duplicate positions; manager secondary role; reactivation guard; normal employee creation and edits.')
finally:
    if document:
        sql(f'DELETE FROM SupplierDocuments WHERE SupplierDocumentId={document}')
    sql(f"DELETE FROM AspNetUserRoles WHERE UserId IN (SELECT Id FROM AspNetUsers WHERE Email='{email}'); DELETE FROM AspNetUsers WHERE Email='{email}';")
    file.unlink(missing_ok=True)
    print('Temporary regression fixtures removed.')
