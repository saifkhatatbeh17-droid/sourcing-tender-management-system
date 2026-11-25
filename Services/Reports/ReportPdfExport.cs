using Microsoft.AspNetCore.Mvc.ViewFeatures;
using MigraDoc.DocumentObjectModel;
using SourcingTenderManagement.Models;
using SourcingTenderManagement.Models.Entities;
using SourcingTenderManagement.Models.TopCommittee;
using SourcingTenderManagement.Services.TopCommittee;
using static SourcingTenderManagement.Services.TopCommittee.TopCommitteeReportExport;
using EvaluationRecord = SourcingTenderManagement.Models.Entities.Evaluation;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;

namespace SourcingTenderManagement.Services.Reports;

// Presentation only: inputs are the same authorized models loaded by HTML actions.
// No database access, AI calls, workflow writes, or watermark application here.
public static class ReportPdfExport
{
    public static byte[] AwardContract(Tender tender, Bid winner)
    {
        var supplier = winner.Invitation.SupplierProfile;
        var report = new Report("Award contract", "Tiryaq");
        report.Field("Tender", $"#{tender.TenderId} · {tender.Title}");
        report.Field("Award date", Date(tender.AwardedAt));
        report.Field("Buyer", "Tiryaq");
        report.Field("Supplier", supplier.CompanyName);
        report.Field("Registration number", supplier.RegistrationNumber);
        report.Field("Tax number", supplier.TaxNumber);
        report.Rows(["Item", "Requested", "Awarded", "Unit price", "Line total"], [9, 4, 4, 4.5, 5],
            winner.BidItems.OrderBy(i => i.PurchaseRequestItemId).Select(i => new[]
            {
                i.PurchaseRequestItem.ItemName, $"{i.PurchaseRequestItem.Quantity:0.###} {i.PurchaseRequestItem.Unit}",
                $"{i.Quantity:0.###} {i.PurchaseRequestItem.Unit}", $"{i.UnitPrice:N2} {winner.Currency}",
                $"{i.Quantity * i.UnitPrice:N2} {winner.Currency}"
            }), "No awarded items recorded.");
        var subtotal = winner.BidItems.Sum(i => i.Quantity * i.UnitPrice);
        report.Field("Subtotal", $"{subtotal:N2} {winner.Currency}");
        report.Field("Tax", $"{winner.TaxAmount:N2} {winner.Currency}");
        report.Field("Extra costs", $"{winner.AdditionalCosts:N2} {winner.Currency}");
        report.Field("Total", $"{subtotal + winner.TaxAmount + winner.AdditionalCosts:N2} {winner.Currency}");
        report.Field("Delivery", $"{winner.DeliveryDays} days");
        report.Field("Payment terms", winner.PaymentTerms);
        report.Field("Offer validity", winner.ValidUntil?.ToString("dd MMM yyyy"));
        return report.Pdf();
    }

    public static byte[] SupplierActivity(SupplierActivityRow supplier)
    {
        var report = new Report("Supplier activity report", "Administration");
        report.Heading(supplier.Company);
        report.Rows(["Supplier ID", "Registration number", "Supply category", "Account status"],
            [4, 6.5, 10.2, 6], [new[] { supplier.SupplierId.ToString(), supplier.RegistrationNumber,
                supplier.Category ?? "Not recorded", supplier.Status }], "");
        report.Heading("Activity summary");
        report.Rows(["Submitted tenders", "Awarded", "Rejected"], [8.9, 8.9, 8.9],
            [new[] { supplier.History.Count.ToString(), supplier.Awarded.ToString(), supplier.Rejected.ToString() }], "");
        report.Heading("Tender history");
        report.Rows(["Tender", "Offer", "Submitted (UTC)", "Total", "Compliance", "Outcome"],
            [8.2, 2.1, 4.5, 4.2, 4.2, 3.5],
            supplier.History.Select(row => new[] { $"#{row.TenderId} - {row.Title}", $"#{row.BidId}",
                Date(row.SubmittedAt), $"{row.Total:N2} {row.Currency}", row.Compliance ?? "Pending", row.Outcome }),
            "No submitted offers are recorded for this supplier.");
        return report.Pdf();
    }

    public static byte[] OfficerReports(IReadOnlyList<EvaluationReport> reports)
    {
        var r=new Report("Evaluation reports for your tenders","Sourcing Officer");
        r.Field("Reports",reports.Count.ToString());
        r.Field("Recommendations recorded",reports.Count(x=>x.RecommendedBidId.HasValue).ToString());
        r.Field("Manager notes recorded",reports.Count(x=>!string.IsNullOrWhiteSpace(x.ManagerSummary)).ToString());
        // Tender reference, status and date match the Reports page wording (OfficerReportLabels).
        r.Rows(["Tender","Status","Committee Findings","Officer Submitted (UTC)","Manager Review"],[4,2.5,9,3,8.2],
            reports.Select(x=>new[]{$"{x.Tender.Title} {OfficerReportLabels.Reference(x)}",OfficerReportLabels.Status(x),string.IsNullOrWhiteSpace(x.CommitteeSummary)?"Not recorded":x.CommitteeSummary,
                OfficerReportLabels.Submitted(x.SubmittedAt),string.IsNullOrWhiteSpace(x.ManagerSummary)?"Awaiting review":x.ManagerSummary}),
            "No committee reports are available for your tenders yet.");
        return r.Pdf();
    }

    public static byte[] OfficerReport(EvaluationReport report)
    {
        var r=new Report("Evaluation report details","Sourcing Officer");
        r.Field("Report",report.EvaluationReportId.ToString());
        r.Field("Tender",report.Tender.Title);r.Field("Status",OfficerReportLabels.Status(report));
        r.Heading("Committee Summary");r.Formatted(report.CommitteeSummary);
        r.Field("Sourcing Officer Summary",report.OfficerSummary);
        r.Field("Prepared By",report.PreparedByUser?.FullName??"Not recorded");
        r.Field("Submitted",OfficerReportLabels.SubmittedDetail(report.SubmittedAt));
        r.Field("Sourcing Manager Summary",report.ManagerSummary??"No manager summary has been recorded.");
        if(report.Bid is {} bid)
        {r.Field("Recommended Bid Supplier",bid.Invitation.SupplierProfile.CompanyName);r.Field("Recommended Bid Status",bid.Status);}
        r.Text("This report is a recommendation and review record. Final award decisions belong to the authorized later workflow stages.");
        return r.Pdf();
    }

    public static byte[] OfficerEvaluation(ViewDataDictionary data)
    {
        var assignments=data["CommitteeAssignments"] as List<CommitteeAssignment>??[];
        var approvals=data["EvaluationApprovals"] as List<TenderApproval>??[];
        var evaluations=data["Evaluations"] as List<EvaluationRecord>??[];
        var criteria=data["EvaluationCriteria"] as List<EvaluationCriterion>??[];
        var bids=data["RecommendationBids"] as List<Bid>??[];
        var report=(EvaluationReport)data["EvaluationReport"]!;
        var tender=(data["EvaluationTenders"] as List<Tender>)!.Single(t=>t.TenderId==report.TenderId);
        var r=new Report("Committee evaluation and review","Sourcing Officer");
        r.Field("Tender",$"#{tender.TenderId} - {tender.Title}");r.Field("Tender status",tender.Status);
        r.Field("Committee assignments",assignments.Count.ToString());r.Field("Evaluation records",evaluations.Count.ToString());
        r.Field("Approved positions",approvals.Count(a=>a.Stage=="Evaluation"&&a.Decision=="Approved").ToString());r.Field("Criteria",criteria.Count.ToString());
        r.Heading("Committee Assignments");
        r.Text($"Submitted or approved evaluator positions: {assignments.Count(a=>evaluations.Any(e=>e.CommitteeAssignmentId==a.CommitteeAssignmentId&&e.Status is "Submitted" or "Approved"))} of {assignments.Count}.");
        r.Rows(["Position","Committee Member","Evaluation Records","Progress"],[2,10,5,9.7],assignments.Select(a=>
        {
            var records=evaluations.Where(e=>e.CommitteeAssignmentId==a.CommitteeAssignmentId).ToList();
            var progress=records.Count==0?"Not started":records.All(e=>e.Status=="Approved")?"Approved":records.Any(e=>e.Status is "Submitted" or "Approved")?"Submitted":"Draft";
            return new[]{a.Position.ToString(),a.User.FullName,records.Count.ToString(),progress};
        }),"No Evaluation committee assignments are recorded for this Tender.");
        r.Heading("Committee Approval Handoff");
        r.Text($"Recorded Evaluation approvals: {assignments.Count(a=>approvals.Any(p=>p.CommitteeAssignmentId==a.CommitteeAssignmentId&&p.Stage=="Evaluation"&&p.Decision=="Approved"))} of {assignments.Count} assigned positions.");
        r.Rows(["Position","Committee Member","Decision","Notes","Decided (UTC)"],[2,6,3,11.7,4],
            approvals.Select(a=>new[]{a.CommitteeAssignment?.Position.ToString()??"-",a.User.FullName,a.Decision,a.Notes??"",Date(a.DecidedAt)}),"No committee approval decisions are recorded yet.");
        r.Text("The committee report reaches you after all four members approve and the final report is generated.");
        r.Heading("Evaluation Criteria");
        r.Rows(["Criterion","Category","Weight","Mandatory","Description"],[5,3.5,2.5,2.5,13.2],
            criteria.Select(c=>new[]{c.Name,c.Category,c.Weight+"%",c.IsMandatory?"Yes":"No",c.Description??""}),"No evaluation criteria are stored for this Tender.");
        r.Heading("Evaluation Records and Scores");
        r.Rows(["Supplier","Committee Member","Status","Submitted (UTC)","Notes and Scores"],[4,5,2.5,3.5,11.7],
            evaluations.Select(e=>new[]{e.Bid.Invitation.SupplierProfile.CompanyName,$"{e.CommitteeAssignment.User.FullName} (position {e.CommitteeAssignment.Position})",e.Status,Date(e.SubmittedAt),
                (e.Notes??"")+"\n"+string.Join("\n",e.EvaluationScores.Select(s=>$"{s.EvaluationCriterion.Name}: {s.Score} - {s.Notes}"))}),"No committee evaluation records are available for this Tender.");
        r.Heading("Committee report review");r.Field("Report status",report.Status);
        r.Field("Recommended offer",bids.FirstOrDefault(b=>b.BidId==report.RecommendedBidId)?.Invitation.SupplierProfile.CompanyName??(report.RecommendedBidId.HasValue?"Offer #"+report.RecommendedBidId:"No offer recommended"));
        r.Heading("Committee report");r.Formatted(report.CommitteeSummary);
        if(!string.IsNullOrWhiteSpace(report.OfficerSummary))r.Field("Officer decision",report.OfficerSummary);
        if(!string.IsNullOrWhiteSpace(report.ManagerSummary))r.Field("Manager review",report.ManagerSummary);
        return r.Pdf();
    }

    public static byte[] CommitteeReport(CommitteeEvaluationPage page)
    {
        var tender=page.Tender!;var report=tender.EvaluationReport!;
        var recommended=page.Offers.FirstOrDefault(b=>b.BidId==report.RecommendedBidId);
        var r=new Report("Combined committee report","Evaluation Committee");
        var rounds = OfferRoundContext.Describe(tender);
        if (rounds.Length > 0 && report.CommitteeSummary?.Contains(rounds, StringComparison.Ordinal) != true) r.Formatted(rounds);
        r.Field("Tender",$"#{tender.TenderId} - {tender.Title}");r.Field("Tender status",CommitteeEvaluationPage.StatusLabel(tender.Status));
        if(page.Criteria.Count>0)
        {
            r.Field("Financial weight",page.Criteria.Where(c=>c.Category=="Financial").Sum(c=>c.Weight).ToString("0.##")+"%");
            r.Field("Technical weight",page.Criteria.Where(c=>c.Category=="Requirements").Sum(c=>c.Weight).ToString("0.##")+"%");
        }
        r.Field("Committee approvals",$"{page.Approvals.Count(a=>a.Decision=="Approved")} / 4");
        r.Heading("Recommended offer");
        if(recommended!=null)
        {
            r.Text(recommended.Invitation.SupplierProfile.CompanyName);
            r.Text($"Offer #{recommended.BidId} - {CommitteeEvaluationPage.Total(recommended):N2} {recommended.Currency} - {recommended.DeliveryDays}-day delivery");
        }
        else r.Text("No offer recommended yet");
        r.Heading("Committee report");r.Field("Report status",CommitteeEvaluationPage.StatusLabel(report.Status));r.Formatted(report.CommitteeSummary);
        if(!string.IsNullOrWhiteSpace(report.OfficerSummary))r.Field("Sourcing officer review",report.OfficerSummary);
        r.Heading("Approval record");r.Text("Shared weights and individual approvals. Four independent member approvals are required.");
        r.Rows(["Member","Approval"],[17,9.7],page.Members.Select(m=>new[]{CommitteeEvaluationPage.MemberName(m.Position)+(m.UserId==page.UserId?" (you)":""),
            page.Approvals.Any(a=>a.UserId==m.UserId&&a.Decision=="Approved")?"Approved":"Awaiting approval"}),"Approvals will appear after the committee evaluation is started.");
        if(page.Locked)r.Text("Evaluation complete. Current tender status: "+tender.Status+".");
        return r.Pdf();
    }

    public static byte[] SavedRecommendation(CommitteeEvaluationPage page)
    {
        var result=page.AiRecommendation!;var scenario=result.Scenarios.First();
        var winner=page.Offers.FirstOrDefault(b=>b.BidId==scenario.RecommendedBidId);
        var r=new Report("Saved AI recommendation","Evaluation Committee");
        r.Field("Tender",$"#{page.Tender!.TenderId} - {page.Tender.Title}");r.Field("Saved (UTC)",Date(page.AiCreatedAt));
        r.Field("Suggested offer",winner?.Invitation.SupplierProfile.CompanyName??"No clear recommendation");
        if(winner!=null)r.Text($"{CommitteeEvaluationPage.Total(winner):N2} {winner.Currency} | Offer #{winner.BidId} | {winner.DeliveryDays}-day delivery");
        r.Text($"{scenario.FinancialWeight:0.##}% financial / {100-scenario.FinancialWeight:0.##}% technical");
        r.Heading("Why this suggestion?");r.Formatted(scenario.Reason);
        r.Heading($"Compare all {page.Offers.Count} offers");
        foreach(var comparison in result.Comparisons)
        {
            var offer=page.Offers.Single(b=>b.BidId==comparison.BidId);
            r.Heading($"Offer #{offer.BidId} - {offer.Invitation.SupplierProfile.CompanyName}");
            r.Field("Strengths",comparison.Strengths);r.Field("Tradeoffs",comparison.Tradeoffs);
        }
        if(result.MissingInformation.Length>0){r.Heading("Information to verify");foreach(var item in result.MissingInformation)r.Text(item);}
        return r.Pdf();
    }

    public static byte[] ManagerDecisions(ManagerReportPage page)
    {
        var r=new Report("Manager reports and decisions","Sourcing Manager");
        r.Text($"Tender title: {page.Search??"All"} | Decision: {page.Decision??"All decisions"} | {page.Decisions.Count} records");
        r.Rows(["Tender","Manager","Decision","Recorded (UTC)","Tender status","Comments"],[4,4,3.2,3.5,3.5,8.5],page.Decisions.Select(x=>new[]{
            $"{x.Tender.Title}\nTender #{x.TenderId}",x.User.FullName,x.Decision=="ReturnedForReview"?"Returned for review":x.Decision,Date(x.DecidedAt),System.Text.RegularExpressions.Regex.Replace(x.Tender.Status,"([a-z])([A-Z])","$1 $2"),string.IsNullOrWhiteSpace(x.Notes)?"No comments":x.Notes}),
            "No matching decisions. Try another title or decision filter. Recorded manager reviews appear here.");
        return r.Pdf();
    }

    public static byte[] ManagerSubmission(ManagerReviewPage page)
    {
        var report=page.Selected!;var recommended=page.Offers.FirstOrDefault(o=>o.BidId==report.RecommendedBidId);
        var r=new Report("Committee submission and supplier comparison","Sourcing Manager");
        var rounds = OfferRoundContext.Describe(report.Tender);
        if (rounds.Length > 0 && report.CommitteeSummary?.Contains(rounds, StringComparison.Ordinal) != true) r.Formatted(rounds);
        r.Field("Tender",$"#{report.TenderId} - {report.Tender.Title}");r.Field("Report",$"#{report.EvaluationReportId} - {report.Status}");
        r.Field("Recommended supplier",recommended?.Invitation.SupplierProfile.CompanyName??"Unavailable");
        r.Heading("Committee report");r.Formatted(report.CommitteeSummary??"No summary was provided.");
        if(!string.IsNullOrWhiteSpace(report.OfficerSummary))r.Field("Sourcing officer summary",report.OfficerSummary);
        r.Heading("Supplier comparison");
        r.Rows(["Supplier","Total including tax and fees","Delivery","Compliance"],[7,5,3,11.7],page.Offers.Select(b=>new[]{b.Invitation.SupplierProfile.CompanyName,
            $"{CommitteeEvaluationPage.Total(b):N2} {b.Currency}",$"{b.DeliveryDays} days",(b.ComplianceStatus??"Unreviewed")+"\n"+b.ComplianceNotes}),"No submitted offers are available.");
        r.Heading("Technical and financial evaluations");
        foreach(var offer in page.Offers)
        {
            r.Heading(offer.Invitation.SupplierProfile.CompanyName);
            if(!string.IsNullOrWhiteSpace(offer.TechnicalDetails))r.Text(offer.TechnicalDetails);
            if(offer.Evaluations.Count==0)r.Text("No submitted evaluations.");
            foreach(var evaluation in offer.Evaluations.OrderBy(e=>e.CommitteeAssignmentId))
            {
                r.Heading($"Evaluation #{evaluation.EvaluationId} ({evaluation.Status})");
                if(evaluation.FinancialMark.HasValue && evaluation.TechnicalMark.HasValue)
                {
                    r.Field("Member",CommitteeEvaluationPage.MemberName(evaluation.CommitteeAssignment.Position));
                    r.Field("Financial mark",$"{evaluation.FinancialMark:0.##} / {report.Tender.CommitteeFinancialWeight:0.##}");
                    r.Field("Technical mark",$"{evaluation.TechnicalMark:0.##} / {100-report.Tender.CommitteeFinancialWeight:0.##}");
                    r.Field("Total mark",$"{evaluation.FinancialMark+evaluation.TechnicalMark:0.##} / 100");
                }
                else r.Rows(["Category","Criterion","Weight","Score","Notes"],[3.5,6,2,2,13.2],evaluation.EvaluationScores.OrderBy(s=>s.EvaluationCriterion.Category).ThenBy(s=>s.EvaluationCriterion.Name)
                    .Select(s=>new[]{s.EvaluationCriterion.Category=="Requirements"?"Technical":"Financial",s.EvaluationCriterion.Name,s.EvaluationCriterion.Weight.ToString("0.##")+"%",s.Score.ToString("0.##"),s.Notes??""}),"No scores recorded.");
                if(!string.IsNullOrWhiteSpace(evaluation.Notes))r.Text(evaluation.Notes);
            }
        }
        if(report.Status=="ManagerApproved"&&!string.IsNullOrWhiteSpace(report.ManagerSummary))r.Field("Refer to the CEO & Chiefs",report.ManagerSummary);
        return r.Pdf();
    }

    public static byte[] UnifiedReport(CommitteeTenderPage page)
    {
        var t=page.Tender;var report=t.Report!;var request=t.PurchaseRequest;
        var r=new Report("Unified evaluation report","CEO & Chiefs");
        r.Field("Tender",$"#{t.TenderId} - {t.Title}");r.Field("Status",CommitteeDisplay.Status(t.Status));
        r.Heading("Purchase request and tender");r.Field("Request",$"#{request.RequestId} - {request.Title}");r.Field("Department",request.Department);
        r.Field("Reason",request.Reason);r.Field("Priority",request.Priority);r.Field("Target date",CommitteeDisplay.Date(request.TargetDate));
        r.Field("Tender description",t.Description);r.Field("Terms",CommitteeDisplay.Text(t.Terms));r.Field("Opens",CommitteeDisplay.Utc(t.OpensAt));r.Field("Submission deadline",CommitteeDisplay.Utc(t.ClosesAt));
        r.Rows(["Item","Quantity","Unit","Specifications"],[5,2.5,2.5,16.7],request.Items.Select(i=>new[]{i.Name,CommitteeDisplay.Quantity(i.Quantity),i.Unit,i.Specifications}),"No request items are recorded.");
        if(t.Documents.Count>0){r.Heading("Tender documents");foreach(var d in t.Documents)r.Text($"{d.OriginalFileName} ({d.SizeBytes:N0} bytes)");}
        r.Heading("Manager recommendation");r.Field("Responsible manager",t.ResponsibleManager?.FullName??"No single referring manager is recorded");
        var nominated=t.Bids.FirstOrDefault(b=>b.IsNominated);r.Field("Nominated bid",nominated==null?"No submitted nomination is available.":$"{nominated.SupplierName} - Bid #{nominated.BidId}");r.Field("Recommendation",CommitteeDisplay.Text(report.ManagerSummary));
        r.Heading("Bid comparison");r.Text("Totals include item prices, taxes and additional costs in each bid's own currency. Evaluation scores are shown as recorded; no currency conversion or combined ranking is applied.");
        r.Rows(["Supplier / bid","Total","Delivery","Compliance","Evaluation results"],[5,4,2.5,4,11.2],t.Bids.Select(b=>new[]{
            $"{b.SupplierName}\nBid #{b.BidId} - {CommitteeDisplay.Status(b.Status)}"+(b.IsNominated?"\nManager nominated":""),$"{CommitteeDisplay.Money(b.Total)} {b.Currency}",$"{b.DeliveryDays} days",CommitteeDisplay.Status(b.ComplianceStatus),
            b.Evaluations.Count==0?"No submitted evaluation results":string.Join("\n",b.Evaluations.Select(e=>$"{e.Evaluator} - {CommitteeDisplay.Status(e.Status)}\n"+(e.Scores.Count==0?"No scores recorded":string.Join("\n",e.Scores.Select(s=>$"{(s.Category=="Requirements"?"Technical":s.Category)}: {s.Criterion} - {CommitteeDisplay.Quantity(s.Score)}/{CommitteeDisplay.Quantity(s.Maximum)}")))))}),"No submitted bids are available for this tender.");
        if(page.SelectedBid is {} bid)
        {
            r.Heading($"Bid #{bid.BidId} - {bid.SupplierName}");r.Field("Status",CommitteeDisplay.Status(bid.Status));r.Field("Submitted",CommitteeDisplay.Utc(bid.SubmittedAt));
            r.Field("Item subtotal",$"{CommitteeDisplay.Money(bid.Subtotal)} {bid.Currency}");r.Field("Tax",$"{CommitteeDisplay.Money(bid.TaxAmount)} {bid.Currency}");
            r.Field("Additional costs",$"{CommitteeDisplay.Money(bid.AdditionalCosts)} {bid.Currency}");r.Field("Total",$"{CommitteeDisplay.Money(bid.Total)} {bid.Currency}");
            r.Field("Delivery timeframe",$"{bid.DeliveryDays} days");r.Field("Payment terms",CommitteeDisplay.Text(bid.PaymentTerms));r.Field("Valid until",CommitteeDisplay.Date(bid.ValidUntil));
            r.Field("Technical details",CommitteeDisplay.Text(bid.TechnicalDetails));r.Field("Compliance",CommitteeDisplay.Status(bid.ComplianceStatus));r.Field("Compliance notes",CommitteeDisplay.Text(bid.ComplianceNotes));
            r.Rows(["Item","Quantity","Unit price ("+bid.Currency+")","Offered specifications"],[5,3,4,14.7],bid.Items.Select(i=>new[]{i.ItemName,CommitteeDisplay.Quantity(i.Quantity),CommitteeDisplay.Money(i.UnitPrice),i.OfferedSpecifications}),"No bid items are recorded.");
            r.Heading("Bid documents");if(bid.Documents.Count==0)r.Text("No bid documents are recorded.");foreach(var d in bid.Documents)r.Text($"{d.OriginalFileName} - {CommitteeDisplay.Text(d.DocumentType)} ({d.SizeBytes:N0} bytes)");
            r.Heading("Evaluation details");if(bid.Evaluations.Count==0)r.Text("No submitted technical or financial evaluations are available.");
            foreach(var e in bid.Evaluations)
            {
                r.Heading(e.Evaluator+" - "+CommitteeDisplay.Status(e.Status));r.Text(CommitteeDisplay.Text(e.Notes));r.Text($"Submitted: {CommitteeDisplay.Utc(e.SubmittedAt)} - Approved: {CommitteeDisplay.Utc(e.ApprovedAt)}");
                r.Rows(["Criterion","Category","Weight","Score","Notes"],[5,3,2.5,2.5,13.7],e.Scores.Select(s=>new[]{s.Criterion,s.Category=="Requirements"?"Technical":s.Category,CommitteeDisplay.Quantity(s.Weight)+"%",CommitteeDisplay.Quantity(s.Score)+"/"+CommitteeDisplay.Quantity(s.Maximum),CommitteeDisplay.Text(s.Notes)}),"No criterion scores are recorded for this evaluation.");
            }
        }
        r.Heading("Unified evaluation report");r.Field("Report",$"#{report.ReportId} - {CommitteeDisplay.Status(report.Status)}");r.Field("Submitted",CommitteeDisplay.Utc(report.SubmittedAt));
        r.Heading("Committee summary");r.Formatted(CommitteeDisplay.Text(report.CommitteeSummary));r.Field("Officer summary",CommitteeDisplay.Text(report.OfficerSummary));r.Field("Manager summary",CommitteeDisplay.Text(report.ManagerSummary));
        r.Heading("Committee records");r.Rows(["Member","Eligibility","Recorded decision","Recorded at"],[7,6,6,7.7],t.Members.Select(m=>
        {var vote=t.Votes.FirstOrDefault(v=>v.UserId==m.UserId);return new[]{m.FullName,m.IsEligible?"Active member":"Not currently eligible",vote==null?"No vote recorded":CommitteeDisplay.Status(vote.Decision),CommitteeDisplay.Utc(vote?.DecidedAt)};}),"No committee members are recorded.");
        foreach(var vote in t.Votes)r.Field(vote.MemberName+" - "+CommitteeDisplay.Status(vote.Decision),CommitteeDisplay.Text(vote.Justification));
        if(t.Votes.Count==0)r.Text("No CEO & Chiefs votes have been recorded.");
        return r.Pdf();
    }

    public static byte[] SupplierSummary(Bid bid)
    {
        var t=bid.Invitation.Tender;var r=new Report("Supplier offer summary","Supplier");
        r.Field("Bid reference",$"BID-{bid.BidId:D6}");r.Field("Tender",$"{t.Title} - TND-{t.TenderId:D6}");r.Field("Status",bid.Status);
        r.Field("Tender status",t.Status);if(t.EvaluationReport!=null)r.Field("Report review status",t.EvaluationReport.Status);
        // Match the existing Offer.cshtml read-only tracker; this is display only.
        var reportStatus=t.EvaluationReport?.Status;
        var stage=bid.Status=="Draft"?0:reportStatus is "TopApproved"||bid.Status is "Accepted" or "Rejected"?7:
            reportStatus=="ManagerApproved"?6:reportStatus=="OfficerSubmitted"?5:reportStatus is "CommitteeApproved" or "ReturnedForReview"?4:
            t.Status=="UnderEvaluation"?3:bid.ComplianceStatus=="Compliant"||bid.Status=="Compliant"?3:
            bid.Status is "Submitted" or "UnderReview" or "NonCompliant"?2:1;
        r.Field("Where your offer is",stage==0?"Draft - not submitted":stage==7?"Final stage":$"Stage {stage} of 7");
        r.Text(bid.Status=="Accepted"?"Your offer was selected by the CEO & Chiefs.":bid.Status=="Rejected"?"The final decision is complete. This offer was not selected.":
            reportStatus=="ManagerApproved"?"The Sourcing Manager approved the recommendation; the CEO & Chiefs is recording its final decision.":
            reportStatus=="OfficerSubmitted"?"The Sourcing Officer approved the consolidated report; it is now with the Sourcing Manager.":
            reportStatus is "CommitteeApproved" or "ReturnedForReview"?"The four-member evaluation report is with the Sourcing Officer for review and approval.":
            t.Status=="UnderEvaluation"?"The evaluation committee is reviewing eligible offers and recording its shared decision.":
            bid.ComplianceStatus=="Compliant"||bid.Status=="Compliant"?"The Sourcing Officer approved the requirements. Committee evaluation begins after the tender closes.":
            bid.ComplianceStatus=="NonCompliant"||bid.Status=="NonCompliant"?"The Sourcing Officer marked this offer non-compliant. Check the officer's findings or clarifications.":
            bid.Status=="Submitted"?"Your offer was sent to the Sourcing Officer for requirements and document review.":"This offer is a draft and has not entered the review workflow.");
        r.Field("Supplier",bid.Invitation.SupplierProfile.CompanyName);r.Field("Submission date",bid.SubmittedAt?.ToLocalTime().ToString("dd MMM yyyy, HH:mm")??"Draft");
        r.Field("Submitted (UTC)",bid.SubmittedAt.HasValue?Date(bid.SubmittedAt):"Draft");r.Field("Delivery time",$"{bid.DeliveryDays} days");r.Field("Offer valid until",bid.ValidUntil?.ToString("dd MMM yyyy")??"Not specified");
        r.Heading("Itemized pricing");r.Rows(["Item","Quantity","Unit price","Line total","Technical response"],[5,3,3.5,3.5,11.7],bid.BidItems.Select(i=>new[]{i.PurchaseRequestItem.ItemName,$"{i.Quantity:N3} {i.PurchaseRequestItem.Unit}",$"{i.UnitPrice:N2} {CurrencyLabel(bid.Currency)}",$"{i.Quantity*i.UnitPrice:N2} {CurrencyLabel(bid.Currency)}",i.OfferedSpecifications}),"No items recorded.");
        r.Field("Item subtotal",$"{bid.BidItems.Sum(i=>i.Quantity*i.UnitPrice):N2} {CurrencyLabel(bid.Currency)}");r.Field("Tax",$"{bid.TaxAmount:N2} {CurrencyLabel(bid.Currency)}");r.Field("Additional costs",$"{bid.AdditionalCosts:N2} {CurrencyLabel(bid.Currency)}");
        r.Field("Total amount",$"{CommitteeEvaluationPage.Total(bid):N2} {CurrencyLabel(bid.Currency)}");
        r.Heading("Commercial terms");r.Field("Payment terms",bid.PaymentTerms??"Not specified");r.Field("Additional technical notes",bid.TechnicalDetails??"Not provided");
        r.Heading("Supporting documents");if(bid.BidDocuments.Count==0)r.Text("No supporting documents uploaded.");
        foreach(var d in bid.BidDocuments)r.Text($"{d.OriginalFileName} - {d.FileSizeBytes/1024d:N0} KB - {d.UploadedAt.ToLocalTime():dd MMM yyyy}");
        return r.Pdf();
    }

    // Supplier screens show Jordanian dinars as "JD" (stored ISO code stays JOD); their PDF follows the screen.
    private static string CurrencyLabel(string? currency)=>currency=="JOD"?"JD":currency??"";

    // Same style as the workspace pages ("14 Nov 2026 12:34 UTC") so a PDF reads like the screen it came from.
    private static string Date(DateTime? value)=>value.HasValue?value.Value.ToString("dd MMM yyyy HH:mm",System.Globalization.CultureInfo.InvariantCulture)+" UTC":"";

    private sealed class Report
    {
        private readonly Document doc;
        private readonly Section section;
        public Report(string title,string owner)
        {
            doc=CreatePdfDocument(title);section=doc.LastSection;
            var p=section.AddParagraph(title);p.Format.Font.Size=20;p.Format.Font.Bold=true;
            Text("Generated "+CommitteeDisplay.Utc(DateTime.UtcNow));
            var footer=section.Footers.Primary.AddParagraph();footer.AddText(owner+" | Page ");footer.AddPageField();footer.AddText(" of ");footer.AddNumPagesField();
        }
        public void Heading(string text)=>TopCommitteeReportExport.Heading(section,text);
        public void Field(string label,string? value){Heading(label);Text(value);}
        public void Text(string? text)
        {
            // Flow full paragraphs; split only over-wide tokens, without removing
            // Unicode characters or imposing a report/row/text length limit.
            AddPdfText(section,text);
        }
        public void Formatted(string? text)
        {
            foreach (var block in ReportContent.Parse(text))
            {
                if (block.Kind == "heading")
                {
                    var heading = section.AddParagraph(block.Lines[0]);
                    heading.Format.Font.Size = 12; heading.Format.Font.Bold = true;
                    heading.Format.Font.Color = block.Lines[0] is "Recommendation" or "Chosen offer"
                        ? Color.FromRgb(0, 123, 126) : Color.FromRgb(21, 44, 50);
                    heading.Format.SpaceBefore = 18; heading.Format.SpaceAfter = 6;
                    heading.Format.KeepWithNext = true;
                }
                else if (block.Kind == "list")
                    foreach (var line in block.Lines) Text("• " + string.Concat(ReportContent.Inline(line).Select(s => s.Text)));
                else Text(string.Concat(ReportContent.Inline(string.Join(" ", block.Lines)).Select(s => s.Text)));
            }
        }
        public void Rows(string[] headers,double[] widths,IEnumerable<string[]> values,string empty)
        {
            var rows=values.ToList();if(rows.Count==0){Text(empty);return;}
            var table=Table(section,headers,widths);foreach(var row in rows)AddRow(table,row);
        }
        public byte[] Pdf()=>RenderPdf(doc);
    }
}

internal static class ReportPdfDownload
{
    // Ordinary FileResult: the existing global watermark filter runs exactly once.
    public static IActionResult Create(Controller controller,Func<byte[]> render,string name)
    {
        try { return controller.File(render(),"application/pdf",name); }
        catch(Exception error) when(error is IOException or InvalidOperationException or ArgumentException
            or NotSupportedException or UnauthorizedAccessException)
        {
            controller.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ReportPdf")
                .LogWarning("Report PDF rendering failed ({FailureType}).",error.GetType().Name);
            return controller.StatusCode(503,new ProblemDetails { Status=503,Title="The complete report could not be exported.",
                Detail="No partial PDF was returned. Check the report font configuration and try again." });
        }
    }
}

// Interpret a small text format. All content is still encoded by Razor; AI HTML is never executed.
public static class ReportContent
{
    private static readonly HashSet<string> Headings = new(StringComparer.OrdinalIgnoreCase)
    {
        "Committee marks", "Member reviews", "Chosen offer", "Why it was chosen", "Comparison", "Observations", "Conclusion",
        "Evaluation basis", "Recommended offer", "Financial review", "Technical review", "Alternatives",
        "Limitations and conclusion", "Decision basis", "Cost and terms", "Technical fit and delivery",
        "Tradeoffs and limitations", "Why this offer", "Cost comparison", "Technical fit", "Tradeoffs",
        "Recommendation", "Cost", "Technical fit & delivery", "Offer score comparison"
    };
    public record Block(string Kind, string[] Lines);
    public record Span(string Text, bool Emphasis);

    public static List<Block> Parse(string? text)
    {
        var blocks = new List<Block>();
        var pending = new List<string>();
        string kind = "paragraph";
        void Flush()
        {
            if (pending.Count > 0) blocks.Add(new(kind, pending.ToArray()));
            pending.Clear();
        }
        foreach (var raw in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) { Flush(); continue; }
            var title = Regex.Replace(line, @"^(?:#{1,6}\s+|\d+[.)]\s+)", "").Trim('*', ' ', ':');
            if (Headings.Contains(title) || Regex.IsMatch(line, @"^#{1,6}\s+.{1,100}$"))
            { Flush(); blocks.Add(new("heading", [title])); continue; }
            var bullet = Regex.IsMatch(line, @"^[-*•]\s+");
            var nextKind = bullet ? "list" : "paragraph";
            if (nextKind != kind) Flush();
            kind = nextKind;
            pending.Add(bullet ? line[2..].TrimStart() : line);
        }
        Flush();
        return blocks;
    }

    public static IEnumerable<Span> Inline(string text)
    {
        var offset = 0;
        foreach (Match match in Regex.Matches(text, @"\*\*(.+?)\*\*"))
        {
            if (match.Index > offset) yield return new(text[offset..match.Index], false);
            yield return new(match.Groups[1].Success ? match.Groups[1].Value : match.Value, true);
            offset = match.Index + match.Length;
        }
        if (offset < text.Length) yield return new(text[offset..], false);
    }
}

/// <summary>
/// Wording for committee reports in the Sourcing Officer workspace. The Reports page and its PDF exports
/// both use these, so a downloaded PDF says exactly what the page says.
/// </summary>
public static class OfficerReportLabels
{
    public static bool NeedsReview(EvaluationReport report) =>
        report.Status == "CommitteeApproved" && report.Tender.Status == "AwaitingOfficerSummary";

    public static string StatusLabel(string status) => status switch
    {
        "Draft" => "In preparation",
        "CommitteeApproved" => "Committee approved",
        "OfficerSubmitted" => "With sourcing manager",
        "ManagerApproved" => "Manager approved",
        "TopApproved" => "CEO & Chiefs approved",
        "ReturnedForReview" => "Returned to committee",
        "ManagerRejected" => "Manager rejected",
        _ => status
    };

    /// <summary>The status as the officer sees it, including the prompt when the report waits on them.</summary>
    public static string Status(EvaluationReport report) =>
        NeedsReview(report) ? "Your review needed" : StatusLabel(report.Status);

    public static string Reference(EvaluationReport report) =>
        $"Report #{report.EvaluationReportId} · Tender #{report.TenderId}";

    /// <summary>Submitted time as shown in the report detail facts, e.g. "14 Nov 2026, 12:34 UTC".</summary>
    public static string SubmittedDetail(DateTime? submittedAt) =>
        submittedAt.HasValue
            ? submittedAt.Value.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : "Not submitted";

    public static string Submitted(DateTime? submittedAt) =>
        submittedAt.HasValue
            ? submittedAt.Value.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC"
            : "Not submitted";
}

public static class ManagerReferralContext
{
    // Saved purchase facts accompany every referral, including manually written referrals.
    public static string Describe(Tender tender, Bid winner)
    {
        var request = tender.PurchaseRequest;
        var text = new StringBuilder().AppendLine("## Tender and purchase requirements")
            .AppendLine($"Tender #{tender.TenderId}: {tender.Title}")
            .AppendLine($"Scope: {Value(tender.Description)}")
            .AppendLine($"Request: {request.Title}. Department: {request.Department.Name}.")
            .AppendLine($"Purchase purpose: {Value(request.Reason)}")
            .AppendLine($"Recorded priority: {Value(request.Priority)}. Required date: {request.TargetDate:yyyy-MM-dd}.")
            .AppendLine("Requested items:");
        foreach (var item in request.PurchaseRequestItems.OrderBy(i => i.PurchaseRequestItemId))
            text.AppendLine($"- {item.ItemName}: {Number(item.Quantity)} {item.Unit}. Specifications: {Value(item.Specifications)}");
        text.AppendLine($"Tender terms: {Value(tender.Terms)}").AppendLine().AppendLine("## Winning offer")
            .AppendLine($"{winner.Invitation.SupplierProfile.CompanyName}, offer #{winner.BidId}: {CommitteeEvaluationPage.Total(winner).ToString("N2", CultureInfo.InvariantCulture)} {winner.Currency} including tax and fees; {winner.DeliveryDays}-day delivery.")
            .AppendLine($"Payment: {Value(winner.PaymentTerms)}")
            .AppendLine($"Supplier-stated technical details: {Value(winner.TechnicalDetails)}");
        text.AppendLine("Purchase requirements are recorded facts; supplier statements require verification. Missing or unclear requirements must be clarified, not inferred.");
        var rounds = OfferRoundContext.Describe(tender);
        if (rounds.Length > 0) text.AppendLine().AppendLine(rounds);
        return text.ToString().Trim();
    }
    private static string Value(string? value) => string.IsNullOrWhiteSpace(value) ? "Not specified" : value.Trim();
    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

public static class OfferRoundContext
{
    public static string Describe(Tender tender)
    {
        if (tender.OfferExtensions.Count == 0 && string.IsNullOrWhiteSpace(tender.OfferShortfallReason)) return "";
        var text = new StringBuilder("## Offer rounds and exception\n");
        foreach (var round in tender.OfferExtensions.OrderBy(r => r.Round))
        {
            text.AppendLine($"Round {round.Round - 1} closed {Utc(round.PreviousDeadline)} with {round.PreviousCompliantOffers} compliant offers.")
                .AppendLine($"Round {round.Round} opened {Utc(round.OpenedAt)}; deadline {Utc(round.Deadline)}.");
        }
        if (!string.IsNullOrWhiteSpace(tender.OfferShortfallReason))
            text.AppendLine("Reason for proceeding with fewer than three compliant offers:").AppendLine(tender.OfferShortfallReason.Trim());
        return text.ToString().Trim();
    }

    private static string Utc(DateTime value) => value.ToString("dd MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
