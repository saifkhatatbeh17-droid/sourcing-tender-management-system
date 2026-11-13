"""Run against a local app: set COMMITTEE_TEST_PASSWORDS to the four passwords
(manager, department, financial, technical), separated by |, then run:
python Checks/committee-cycle.py http://127.0.0.1:5082
Creates temporary SQL fixtures and removes them in finally. Requires local sqlcmd.
"""
import http.cookiejar
import os
import re
import subprocess
import sys
import uuid
import urllib.error
import urllib.parse
import urllib.request

base = sys.argv[1] if len(sys.argv) > 1 else 'http://127.0.0.1:5082'
prefix = 'CommitteeCheck-' + uuid.uuid4().hex
passwords = os.environ['COMMITTEE_TEST_PASSWORDS'].split('|')
assert len(passwords) == 4, 'Supply exactly four passwords.'

def sql(query):
    result = subprocess.run(['sqlcmd', '-S', 'localhost', '-E', '-C', '-I', '-b',
        '-d', 'SourcingTenderManagementCodeFirst', '-h', '-1', '-W', '-Q', 'SET NOCOUNT ON; ' + query],
        capture_output=True, text=True)
    assert result.returncode == 0, result.stderr + result.stdout
    return result.stdout.strip()

def session(email, password):
    client = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
    html = client.open(base + '/Account/Login').read().decode()
    token = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', html).group(1)
    payload = urllib.parse.urlencode({'Email': email, 'Password': password, '__RequestVerificationToken': token}).encode()
    response = client.open(base + '/Account/Login', payload)
    assert '/EvaluationCommittee' in response.url, 'Committee login failed.'
    return client

def page(client, tender):
    return client.open(base + '/EvaluationCommittee/Evaluations?id=' + str(tender)).read().decode()

def post(client, action, tender, values=None, csrf=True):
    html = page(client, tender)
    values = dict(values or {}, id=tender)
    if csrf:
        values['__RequestVerificationToken'] = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', html).group(1)
    try:
        response = client.open(base + '/EvaluationCommittee/' + action, urllib.parse.urlencode(values).encode())
        return response.status, response.read().decode()
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode()

cleanup = f"""
SET XACT_ABORT ON; BEGIN TRANSACTION;
DECLARE @ids TABLE (Id int); INSERT @ids SELECT TenderId FROM Tenders WHERE Title=N'{prefix}';
DELETE FROM AiAnalyses WHERE TenderId IN (SELECT Id FROM @ids);
DELETE FROM TenderApprovals WHERE TenderId IN (SELECT Id FROM @ids);
DELETE FROM EvaluationScores WHERE TenderId IN (SELECT Id FROM @ids);
DELETE FROM Evaluations WHERE TenderId IN (SELECT Id FROM @ids);
DELETE FROM EvaluationReports WHERE TenderId IN (SELECT Id FROM @ids);
DELETE FROM CommitteeAssignments WHERE TenderId IN (SELECT Id FROM @ids);
DELETE FROM EvaluationCriteria WHERE TenderId IN (SELECT Id FROM @ids);
DELETE bi FROM BidItems bi JOIN Bids b ON bi.BidId=b.BidId WHERE b.TenderId IN (SELECT Id FROM @ids);
DELETE FROM Bids WHERE TenderId IN (SELECT Id FROM @ids);
DELETE FROM Invitations WHERE TenderId IN (SELECT Id FROM @ids);
DELETE FROM Tenders WHERE TenderId IN (SELECT Id FROM @ids);
DELETE i FROM PurchaseRequestItems i JOIN PurchaseRequests r ON i.PurchaseRequestId=r.PurchaseRequestId WHERE r.Title=N'{prefix}';
DELETE FROM PurchaseRequests WHERE Title=N'{prefix}'; COMMIT;
"""
try:
    tender, bid = map(int, sql(f"""
    SET XACT_ABORT ON; BEGIN TRANSACTION;
    DECLARE @officer nvarchar(450)=(SELECT Id FROM AspNetUsers WHERE Email='officer@tender.local');
    DECLARE @department nvarchar(450)=(SELECT Id FROM AspNetUsers WHERE Email='department@tender.local');
    INSERT PurchaseRequests(DepartmentId,RequestedByUserId,Title,Reason,Priority,TargetDate,Status)
    SELECT TOP(1) DepartmentId,@department,N'{prefix}','Temporary committee check','Normal',CONVERT(date,GETDATE()),'ConvertedToTender' FROM Departments;
    DECLARE @request int=SCOPE_IDENTITY();
    INSERT PurchaseRequestItems(PurchaseRequestId,ItemName,Quantity,Unit,Specifications) VALUES(@request,'Test laptop',2,'Each','16 GB RAM');
    DECLARE @item int=SCOPE_IDENTITY();
    INSERT Tenders(PurchaseRequestId,CreatedByUserId,Title,Description,OpensAt,ClosesAt,Status)
    VALUES(@request,@officer,N'{prefix}','Temporary committee check',DATEADD(day,-2,GETDATE()),DATEADD(day,-1,GETDATE()),'UnderEvaluation');
    DECLARE @tender int=SCOPE_IDENTITY();
    INSERT Invitations(TenderId,SupplierProfileId,InvitedByUserId,Status)
    SELECT TOP(1) @tender,SupplierProfileId,@officer,'Accepted' FROM SupplierProfiles;
    DECLARE @invitation int=SCOPE_IDENTITY();
    INSERT Bids(InvitationId,TenderId,Currency,DeliveryDays,TechnicalDetails,TaxAmount,AdditionalCosts,Status,ComplianceStatus)
    VALUES(@invitation,@tender,'JOD',10,'Temporary offer: 16 GB RAM',10,5,'Submitted','Compliant');
    DECLARE @bid int=SCOPE_IDENTITY();
    INSERT BidItems(BidId,PurchaseRequestItemId,Quantity,UnitPrice,OfferedSpecifications) VALUES(@bid,@item,2,100,'16 GB RAM');
    COMMIT; SELECT @tender,@bid;
    """).split())
    clients = [session('evaluation.' + name + '@tender.local', password) for name, password in zip(
        ['manager', 'department', 'financial', 'technical'], passwords)]
    assert '215.00 JOD' in page(clients[0], tender), 'Offer total was not rendered.'
    assert post(clients[0], 'Start', tender, csrf=False)[0] == 400, 'Missing CSRF token accepted.'
    assert post(clients[0], 'Start', tender)[0] == 200
    assert sql(f"SELECT COUNT(*) FROM CommitteeAssignments WHERE TenderId={tender}") == '4'
    assert post(clients[0], 'Approve', tender)[0] == 200
    assert sql(f"SELECT COUNT(*) FROM TenderApprovals WHERE TenderId={tender}") == '0', 'Incomplete evaluation approved.'
    criteria = {'criteria[0].Name': 'Price', 'criteria[0].Category': 'Financial', 'criteria[0].Weight': 40,
        'criteria[1].Name': 'Specifications', 'criteria[1].Category': 'Requirements', 'criteria[1].Weight': 60}
    post(clients[0], 'SaveCriteria', tender, dict(criteria, **{'criteria[1].Weight': 50}))
    assert sql(f"SELECT SUM(Weight) FROM EvaluationCriteria WHERE TenderId={tender}") == '100.00', 'Invalid weights saved.'
    assert post(clients[0], 'SaveCriteria', tender, criteria)[0] == 200
    financial = int(sql(f"SELECT EvaluationCriterionId FROM EvaluationCriteria WHERE TenderId={tender} AND Category='Financial'"))
    requirements = int(sql(f"SELECT EvaluationCriterionId FROM EvaluationCriteria WHERE TenderId={tender} AND Category='Requirements'"))
    def scores(criterion, score):
        return {'scores[0].BidId': bid, 'scores[0].CriterionId': criterion, 'scores[0].Score': score, 'notes': 'Committee check'}
    post(clients[2], 'SaveScores', tender, scores(requirements, 80))
    post(clients[3], 'SaveScores', tender, scores(requirements, 101))
    assert sql(f"SELECT COUNT(*) FROM Evaluations WHERE TenderId={tender}") == '0', 'Invalid or foreign score saved.'
    # Approve directly: no saved forms or report exist, and the other members are not ready.
    assert post(clients[0], 'Approve', tender, scores(requirements, 85))[0] == 200
    assert sql(f"SELECT COUNT(*) FROM TenderApprovals WHERE TenderId={tender}") == '1'
    assert sql(f"SELECT COUNT(*) FROM Evaluations WHERE TenderId={tender}") == '1'
    assert sql(f"SELECT COUNT(*) FROM Evaluations WHERE TenderId={tender} AND Status='Approved'") == '1'
    assert sql(f"SELECT Status FROM Tenders WHERE TenderId={tender}") == 'UnderEvaluation'
    post(clients[0], 'SaveScores', tender, scores(requirements, 0))
    post(clients[0], 'Approve', tender, scores(requirements, 0))
    assert sql(f"SELECT COUNT(*) FROM TenderApprovals WHERE TenderId={tender}") == '1', 'Duplicate approval accepted.'
    assert sql(f"SELECT Score FROM EvaluationScores WHERE TenderId={tender}") == '85.00', 'Approved scores were edited.'
    # The first approval must not block a different member from saving or approving.
    assert post(clients[1], 'SaveScores', tender, scores(requirements, 80))[0] == 200
    assert sql(f"SELECT COUNT(*) FROM Evaluations WHERE TenderId={tender}") == '2'
    assert post(clients[3], 'SaveReport', tender, {'recommendedBidId': bid, 'summary': 'Not authorized'})[0] == 200
    assert sql(f"SELECT COUNT(*) FROM EvaluationReports WHERE TenderId={tender} AND RecommendedBidId IS NOT NULL") == '0'
    for index, client in enumerate(clients[1:]):
        criterion = financial if index == 1 else requirements
        assert post(client, 'Approve', tender, scores(criterion, [80, 100, 90][index]))[0] == 200
        expected = 'AwaitingOfficerSummary' if index == 2 else 'UnderEvaluation'
        assert sql(f"SELECT Status FROM Tenders WHERE TenderId={tender}") == expected, 'Wrong handoff timing.'
    assert '91' in page(clients[0], tender), 'Expected combined score missing.'
    assert sql(f"SELECT Status FROM EvaluationReports WHERE TenderId={tender}") == 'CommitteeApproved'
    assert sql(f"SELECT RecommendedBidId FROM EvaluationReports WHERE TenderId={tender}") == str(bid), 'Recommendation not completed.'
    assert sql(f"SELECT COUNT(*) FROM EvaluationReports WHERE TenderId={tender} AND CommitteeSummary IS NOT NULL") == '1'
    assert sql(f"SELECT COUNT(*) FROM TenderApprovals WHERE TenderId={tender}") == '4'
    assert sql(f"SELECT COUNT(*) FROM Evaluations WHERE TenderId={tender} AND Status='Approved'") == '4'
    if '--ai' in sys.argv:
        assert post(clients[0], 'Analyze', tender)[0] == 200
        assert sql(f"SELECT COUNT(*) FROM AiAnalyses WHERE TenderId={tender}") == '3', 'AI analysis did not save all three ratios.'
    assert post(clients[3], 'SaveScores', tender, scores(requirements, 0))[0] == 400
    print('PASS: direct approval without prior saves/report, optional save, independent member forms, score ownership/ranges, approved-form locking, duplicate prevention, automatic report and recommendation, four-member handoff' + (', three real Ollama analyses after approval.' if '--ai' in sys.argv else '.'))
finally:
    sql(cleanup)
    print('Temporary fixtures removed. Existing demo data preserved.')
