using System.Globalization;
using System.Data.Common;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SourcingTenderManagement.Data;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Services.Reports.Hub;
using PdfDocument = UglyToad.PdfPig.PdfDocument;

namespace SourcingTenderManagement.Checks;
// Self-test, not part of the website. Tests tenders report. Not run from Program.cs: a temporary test host gives it an in-memory SQLite database (see REPORTS-INTEGRATION-NOTES.md).

public static class TendersReportChecks
{
    public static async Task RunAsync(ApplicationDbContext db, string outputDirectory)
    {
        if (db.Database.ProviderName != "Microsoft.EntityFrameworkCore.Sqlite"
            || db.Database.GetDbConnection().ConnectionString != "Data Source=:memory:")
            throw new InvalidOperationException("Tender report checks require SQLite in-memory only.");
        var checks = 0;
        void Require(bool condition, string label)
        {
            if (!condition) throw new Exception("Tender report check failed: " + label);
            checks++;
        }
        var ownDepartmentId = await db.Users.Where(user => user.Id == "hub-Department")
            .Select(user => user.DepartmentId!.Value)
            .SingleAsync();
        var privateDepartment = new Department { Name = "TNCHECK PRIVATE-DEPARTMENT", IsActive = true };
        var emptyDepartment = new Department { Name = "TNCHECK empty department", IsActive = true };
        db.Departments.AddRange(privateDepartment, emptyDepartment);
        foreach (var id in new[] { "tncheck-supplier-a", "tncheck-supplier-b", "tncheck-supplier-c", "tncheck-private-supplier", "tncheck-other-officer" })
            db.Users.Add(new ApplicationUser { Id = id, FullName = id });
        await db.SaveChangesAsync();
        var suppliers = new[] { "a", "b", "c", "private-supplier" }.Select(value => new SupplierProfile
        {
            UserId = value == "private-supplier" ? "tncheck-private-supplier" : "tncheck-supplier-" + value,
            CompanyName = "TNCHECK supplier " + value, ContactName = "Synthetic", RegistrationNumber = "TNCHECK-" + value, Status = "Approved"
        }).ToList();
        db.SupplierProfiles.AddRange(suppliers);
        var request = new PurchaseRequest { DepartmentId = ownDepartmentId, RequestedByUserId = "hub-Department", ReviewedByUserId = "hub-SourcingOfficer",
            Title = "TNCHECK own request", Status = "ConvertedToTender", Reason = "Isolated checks", Priority = "Normal" };
        var privateRequest = new PurchaseRequest { DepartmentId = privateDepartment.DepartmentId, RequestedByUserId = "hub-Admin",
            Title = "TNCHECK private request", Status = "ConvertedToTender", Reason = "Isolated checks", Priority = "Normal" };
        db.PurchaseRequests.AddRange(request, privateRequest);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        var statuses = new[] { "Published",
            "Open",
            "Closed",
            "UnderEvaluation",
            "AwaitingOfficerSummary",
            "AwaitingManagerReview",
            "AwaitingCommitteeDecision",
            "Awarded",
            "Cancelled" };
        var tenders = Enumerable.Range(0, 32).Select(index => new Tender
        {
            PurchaseRequestId = request.PurchaseRequestId, CreatedByUserId = "hub-SourcingOfficer", Title = "TNCHECK own " + index.ToString("D2", CultureInfo.InvariantCulture),
            Description = "Synthetic check", Category = "Pharmaceuticals", Status = index < statuses.Length ? statuses[index] : "Published",
            CreatedAt = now.AddDays(-40), OpensAt = now
                .AddDays(-35), ClosesAt = index >= 2
                && index <= 8 ? now
                .AddDays(-20) : now
                .AddDays(10)
        }).ToList();
        var privateTender = new Tender { PurchaseRequestId = privateRequest.PurchaseRequestId, CreatedByUserId = "tncheck-other-officer",
            Title = "TNCHECK PRIVATE-ONLY", Description = "Synthetic", Status = "UnderEvaluation", CreatedAt = now.AddDays(-40), OpensAt = now.AddDays(-35), ClosesAt = now.AddDays(-30) };
        db.Tenders.AddRange(tenders); db.Tenders.Add(privateTender);
        await db.SaveChangesAsync();
        Invitation Invite(Tender tender, SupplierProfile supplier) => new()
        {
            TenderId = tender.TenderId, SupplierProfileId = supplier.SupplierProfileId, InvitedByUserId = tender.CreatedByUserId,
            SentAt = now.AddDays(-34), Status = "Accepted"
        };
        var invitations = new List<Invitation> { Invite(tenders[0], suppliers[0]), Invite(tenders[0], suppliers[1]), Invite(tenders[0], suppliers[2]) };
        invitations.AddRange(new[] { 2, 3, 4, 5, 6, 7 }.Select(index => Invite(tenders[index], suppliers[0])));
        var privateInvitation = Invite(privateTender, suppliers[3]);
        db.Invitations.AddRange(invitations); db.Invitations.Add(privateInvitation);
        await db.SaveChangesAsync();
        Bid Offer(Invitation invitation, string status, bool submitted) => new()
        {
            InvitationId = invitation.InvitationId, TenderId = invitation.TenderId, Currency = "JOD", Status = status,
            SubmittedAt = submitted ? now.AddDays(-31) : null, CreatedAt = now
                .AddDays(-32), ComplianceStatus = submitted ? "Compliant" : null
        };
        var offers = invitations.Select(invitation => Offer(invitation,
            invitation == invitations[1] ? "Draft" : invitation == invitations[2]
                || invitation.TenderId == tenders[2].TenderId ? "Withdrawn" : "Submitted",
            invitation != invitations[1])).ToList();
        db.Bids.AddRange(offers); db.Bids.Add(Offer(privateInvitation, "Submitted", true));
        await db.SaveChangesAsync();
        tenders[7].AwardedBidId = offers.Single(offer => offer.TenderId == tenders[7].TenderId).BidId;
        tenders[7].AwardedAt = now.AddDays(-5);
        db.EvaluationReports.AddRange(new EvaluationReport { TenderId = tenders[5].TenderId, Status = "OfficerSubmitted", SubmittedAt = now.AddDays(-10) },
            new EvaluationReport { TenderId = tenders[7].TenderId, Status = "TopApproved", SubmittedAt = now.AddDays(-10) });
        db.CommitteeAssignments.AddRange(new CommitteeAssignment { TenderId = tenders[3].TenderId, UserId = "hub-EvaluationCommittee", CommitteeType = "Evaluation", Position = 1 },
            new CommitteeAssignment { TenderId = tenders[6].TenderId, UserId = "hub-TopCommittee", CommitteeType = "Top", Position = 1 });
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().Build();
        var report = new TendersReport(db, config);
        var officerScope = new ReportScope("hub-SourcingOfficer", RoleNames.SourcingOfficer, ownDepartmentId, false);
        var departmentScope = new ReportScope("hub-Department", RoleNames.Department, ownDepartmentId, false);
        var adminScope = new ReportScope("hub-Admin", RoleNames.Admin, null, true);
        var query = new ReportQuery(Search: "TNCHECK");
        var officer = await report.RunAsync(query, officerScope, default);
        string Kpi(ReportResult result, string label) => result.Kpis.Single(kpi => kpi.Label == label).Value;
        Require(officer.Table.Total == 32 && Kpi(officer, "Total tenders") == "32", "correct owned count");
        foreach (var status in statuses)
            Require(Kpi(officer, status) == (status == "Published" ? "24" : "1"), "status count " + status);
        Require(Kpi(officer, "Invitations") == "9" && Kpi(officer, "Received offers") == "6" && Kpi(officer, "Supplier participation rate") == "66.7%",
            "participation matches AI definition, ignores drafts and withdrawn offers");
        Require(Kpi(officer, "Tenders with zero offers") == "2", "zero offers require passed deadline and ignore withdrawn offers");
        Require(Kpi(officer, "Delayed evaluations") == "Unavailable" && Kpi(officer, "Evaluation age warnings (>7 days)") == "1" &&
            officer.Notes.Any(note => note.Contains("not an SLA")), "no fabricated delay, explicit closure-age proxy");
        Require(officer.Chart is { Title: "Tenders by status", Kind: "bar" } && officer.Chart.Points.Sum(point => point.Value) == 32,
            "status chart uses visible records");
        Require(officer.Table.Rows.Count == 25
            && officer.Table.Rows.All(row => row.Link!.StartsWith("/SourcingOfficer/Offers?tenderId=")), "pagination and officer links");
        Require((await report.RunAsync(query with { Page = 2 }, officerScope, default)).Table.Rows.Count == 7, "second page contains remaining rows");
        var exported = await report.RunAsync(query with { ForExport = true, Page = 55 }, officerScope, default);
        var secondPage = await report.RunAsync(query with { Page = 2 }, officerScope, default);
        Require(officer.Table.Rows.Concat(secondPage.Table.Rows).Select(row => string.Join("|", row.Cells)).SequenceEqual(
            exported.Table.Rows.Select(row => string.Join("|", row.Cells))), "SQL pages preserve exact export ordering and values");
        Require(officer.Kpis.SequenceEqual(secondPage.Kpis) && officer.Kpis.SequenceEqual(exported.Kpis) &&
            officer.Chart!.Points.SequenceEqual(exported.Chart!.Points), "SQL pagination does not alter KPIs or chart values");
        Require(exported.Table.Rows.Count == 32 && exported.Table.Rows.Select(row => row.Cells[0]).ToHashSet().SetEquals(tenders.Select(tender => "#" + tender.TenderId)),
            "export ignores normal page and retains exact officer ownership");
        var awardRow = exported.Table.Rows.Single(row => row.Cells[0] == "#" + tenders[7].TenderId);
        Require(awardRow.Cells[11] == suppliers[0].CompanyName
            && awardRow.Cells[12] == "TopApproved"
            && awardRow.Cells[13] == "—", "actual award and report status, completed evaluation not timed");
        var evaluation = exported.Table.Rows.Single(row => row.Cells[4] == "UnderEvaluation");
        Require(double.Parse(evaluation.Cells[13], CultureInfo.InvariantCulture) >= 20
            && evaluation.Cells[14]
            .StartsWith("Unavailable"), "elapsed closure age is not called actual delay");
        var statusDrill = await report.RunAsync(query with { Drill = "status:Closed" }, officerScope, default);
        Require(statusDrill.Table.Total == 1
            && statusDrill.Table.Rows[0].Cells[4] == "Closed"
            && Kpi(statusDrill, "Total tenders") == "32", "status drill narrows table only");
        var activity = await report.RunAsync(query with { Drill = "chart:activity" }, officerScope, default);
        Require(activity.Chart!.Title == "Tender activity"
            && activity.Chart.Points.Sum(point => point.Value) == 32
            && activity.Table.Total == 32, "second chart partitions activity without narrowing table");
        Require((await report.RunAsync(query with { Drill = "activity:received" }, officerScope, default)).Table.Total == 6, "received activity drill");
        Require((await report.RunAsync(query with { Drill = "activity:zero-offers" }, officerScope, default)).Table.Total == 2, "zero offer drill");
        Require((await report.RunAsync(query with { Drill = "activity:evaluation-age" }, officerScope, default)).Table.Total == 1, "evaluation age drill");
        Require((await report.RunAsync(query with { Status = "Closed", Drill = "activity:received" },
            officerScope,
            default)).Table.Total == 0, "filters intersect drills");
        Require((await report.RunAsync(query with { SupplierId = suppliers[0].SupplierProfileId, DepartmentId = ownDepartmentId,
            Status = "Published", From = DateOnly.FromDateTime(now.AddDays(-41)), To = DateOnly.FromDateTime(now) }, officerScope, default)).Table.Total == 1,
            "date, supplier, department, status and search combine");
        var eventRange = await report.RunAsync(query with { To = DateOnly.FromDateTime(now.AddDays(-39)) }, officerScope, default);
        Require(eventRange.Table.Total == 32 && Kpi(eventRange, "Invitations") == "0" && Kpi(eventRange, "Received offers") == "0" &&
            Kpi(eventRange, "Supplier participation rate") == "—" && Kpi(eventRange, "Tenders with zero offers") == "2", "event range counts and lifetime zero-offer meaning");
        var department = await report.RunAsync(query with { ForExport = true }, departmentScope, default);
        Require(department.Table.Total == 32
            && department.Table.Rows.All(row => row.Link == null), "Department scope only own requests, no unauthorized tender links");
        Require((await report.RunAsync(query with { DepartmentId = privateDepartment.DepartmentId }, departmentScope, default)).Table.Total == 0,
            "forged department cannot escape scope");
        Require((await report.RunAsync(query with { SupplierId = suppliers[3].SupplierProfileId }, officerScope, default)).Table.Total == 0,
            "private supplier cannot escape officer scope");
        var committeeScope = new ReportScope("hub-EvaluationCommittee", RoleNames.EvaluationCommittee, null, false);
        var committee = await report.RunAsync(query, committeeScope, default);
        Require(committee.Table.Total == 1 && committee.Table.Rows[0].Link == "/EvaluationCommittee/Evaluations?id=" + tenders[3].TenderId,
            "Evaluation Committee assigned tenders only, unassigned UnderEvaluation excluded");
        var topScope = new ReportScope("hub-TopCommittee", RoleNames.TopCommittee, null, false);
        var top = await report.RunAsync(query, topScope, default);
        Require(top.Table.Total == 1
            && top.Table.Rows[0].Link == "/TopCommittee/TenderDetails/" + tenders[6].TenderId, "Top Committee assignment and link");
        var managerScope = new ReportScope("hub-SourcingManager", RoleNames.SourcingManager, null, false);
        var manager = await report.RunAsync(query, managerScope, default);
        Require(manager.Table.Total == 1
            && manager.Table.Rows[0].Link!.StartsWith("/SourcingManager/Submissions?id="), "Manager queue matches existing report scope and safe review link");
        db.TenderApprovals.Add(new TenderApproval { TenderId = tenders[7].TenderId,
            UserId = managerScope.UserId,
            Stage = "Manager",
            Decision = "Approved",
            DecidedAt = now.AddDays(-8) });
        await db.SaveChangesAsync();
        manager = await report.RunAsync(query, managerScope, default);
        Require(manager.Table.Total == 2
            && manager.Table.Rows.Single(row => row.Cells[4] == "Awarded").Link == null, "Manager history visible without invalid queue link");
        var admin = await report.RunAsync(query, adminScope, default);
        Require(admin.Table.Total == 33
            && admin.Table.Rows.All(row => row.Link == null), "Admin scope and no invented details route");
        Require((await report.RunAsync(query, officerScope with { Role = RoleNames.Supplier }, default)).Table.Total == 0, "Supplier scope fails closed");
        Require((await report.RunAsync(query, officerScope with { IsAdmin = true }, default)).Table.Total == 0, "forged admin scope fails closed");
        var empty = await report.RunAsync(query, departmentScope with { DepartmentId = emptyDepartment.DepartmentId }, default);
        Require(empty.Table.Total == 0
            && Kpi(empty, "Total tenders") == "0"
            && Kpi(empty, "Supplier participation rate") == "—", "empty data has no misleading participation rate");
        var invalid = await report.RunAsync(query with { Status = "Draft", Drill = "activity:invalid", Page = -20 }, officerScope, default);
        Require(invalid.Table.Total == 32
            && invalid.Table.Rows.Count == 25
            && invalid.Notes.Any(note => note.Contains("unsupported")), "invalid filters safe, no invented statuses");
        var configured = new TendersReport(db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Reports:EvaluationDelayDays"] = "30" })
            .Build());
        Require(Kpi(await configured.RunAsync(query, officerScope, default), "Evaluation age warnings (>30 days)") == "0", "configured age threshold honored");
        var malformedConfig = new TendersReport(db, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Reports:EvaluationDelayDays"] = "bad" })
            .Build());
        Require((await malformedConfig.RunAsync(query, officerScope, default)).Notes.Any(note => note
            .Contains("setting was invalid")), "invalid age setting handled safely");
        var choices = await new ReportFilterOptions(db).LoadAsync(report, officerScope, default);
        Require(choices.Departments.All(option => option.Id != privateDepartment.DepartmentId) && choices.Suppliers.All(option => option.Id != suppliers[3].SupplierProfileId),
            "filter options cannot expose private departments or suppliers");
        Directory.CreateDirectory(outputDirectory);
        foreach (var (scope,
            label) in new[] { (officerScope,"officer"),
            (departmentScope,"department"),
            (committeeScope,"committee"),
            (topScope,"top"),
            (managerScope,"manager") })
        {
            var result = await report.RunAsync(query with { ForExport = true }, scope, default);
            var exporter = new ReportExport();
            var xlsx = exporter.Excel(report, query, result, now);
            using (var workbook = new XLWorkbook(new MemoryStream(xlsx)))
                Require(workbook.Worksheet("Data").LastRowUsed()!.RowNumber() == result.Table.Total + 1 &&
                    workbook.Worksheet("Data").Cell(1, 1).GetString() == "Tender Number" && !workbook.Worksheet("Data").CellsUsed().Any(cell => cell.GetString().Contains("PRIVATE-ONLY")), label + " Excel scope and headers");
            var pdf = exporter.Pdf(report, query, result, now);
            using (var document = PdfDocument.Open(pdf))
            {
                var text = string.Join(" ", document.GetPages().Select(page => page.Text));
                Require(System.Text.Encoding.ASCII.GetString(pdf,0,4) == "%PDF"
                    && text
                    .Contains("TNCHECK")
                    && !text
                    .Contains("PRIVATE-ONLY"), label + " PDF scope");
            }
            await File.WriteAllBytesAsync(Path.Combine(outputDirectory, "tenders-" + label + ".xlsx"), xlsx);
            await File.WriteAllBytesAsync(Path.Combine(outputDirectory, "tenders-" + label + ".pdf"), pdf);
        }
        // SQL Server translation check only: intercept and refuse connection opening.
        // No SQL Server connection or command is allowed by this probe.
        var blockedConnection = new BlockSqlConnection();
        var sqlOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=reports-translation.invalid;Database=NeverConnect;Integrated Security=true")
            .AddInterceptors(blockedConnection).Options;
        await using var sqlContext = new ApplicationDbContext(sqlOptions);
        try
        {
            await new TendersReport(sqlContext, new ConfigurationBuilder().Build()).RunAsync(query, adminScope, default);
            throw new Exception("SQL translation probe did not block the connection.");
        }
        catch (TranslationCompleteException)
        {
            Require(blockedConnection.Blocked, "SQL Server aggregate query translates before connection is refused");
        }
        Console.WriteLine($"Tender report checks passed: {checks}. SQLite data only; SQL Server translation probe blocks connection opening.");
    }

    private sealed class TranslationCompleteException : Exception;
    private sealed class BlockSqlConnection : DbConnectionInterceptor
    {
        public bool Blocked { get; private set; }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Blocked = true;
            throw new TranslationCompleteException();
        }
    }
}
