# Workspace page map

Related screens are sections, tabs, or popups on one parent page. Business form bodies remain team implementation areas.

## Admin

| Route | View |
|---|---|
| `/Admin/Dashboard` | `Views/Admin/Dashboard.cshtml` |
| `/Admin/Users` | `Views/Admin/Users.cshtml` |
| `/Admin/Departments` | `Views/Admin/Departments.cshtml` |
| `/Admin/SupplierApplications` | `Views/Admin/SupplierApplications.cshtml` |
| `/Admin/Suppliers` | `Views/Admin/Suppliers.cshtml` |

## Department

| Route | View |
|---|---|
| `/Department/Dashboard` | `Views/Department/Dashboard.cshtml` |
| `/Department/Requests` | `Views/Department/Requests.cshtml` |
| `/Department/Clarifications` | `Views/Department/Clarifications.cshtml` |

## SourcingOfficer

| Route | View |
|---|---|
| `/SourcingOfficer/Dashboard` | `Views/SourcingOfficer/Dashboard.cshtml` |
| `/SourcingOfficer/Requests` | `Views/SourcingOfficer/Requests.cshtml` |
| `/SourcingOfficer/Tenders` | `Views/SourcingOfficer/Tenders.cshtml` |
| `/SourcingOfficer/Invitations` | `Views/SourcingOfficer/Invitations.cshtml` |
| `/SourcingOfficer/Offers` | `Views/SourcingOfficer/Offers.cshtml` |
| `/SourcingOfficer/Compliance` | `Views/SourcingOfficer/Compliance.cshtml` |
| `/SourcingOfficer/Comparison` | `Views/SourcingOfficer/Comparison.cshtml` |
| `/SourcingOfficer/Evaluations` | `Views/SourcingOfficer/Evaluations.cshtml` |
| `/SourcingOfficer/Clarifications` | `Views/SourcingOfficer/Clarifications.cshtml` |
| `/SourcingOfficer/Reports` | `Views/SourcingOfficer/Reports.cshtml` |

## EvaluationCommittee

| Route | View |
|---|---|
| `/EvaluationCommittee/Dashboard` | `Views/EvaluationCommittee/Dashboard.cshtml` |
| `/EvaluationCommittee/Evaluations` | `Views/EvaluationCommittee/Evaluations.cshtml` |
| `/EvaluationCommittee/Reports` | `Views/EvaluationCommittee/Reports.cshtml` |
| `/EvaluationCommittee/Clarifications` | `Views/EvaluationCommittee/Clarifications.cshtml` |

## SourcingManager

| Route | View |
|---|---|
| `/SourcingManager/Dashboard` | `Views/SourcingManager/Dashboard.cshtml` |
| `/SourcingManager/Submissions` | `Views/SourcingManager/Submissions.cshtml` |
| `/SourcingManager/ChosenOffers` | `Views/SourcingManager/ChosenOffers.cshtml` |
| `/SourcingManager/Reports` | `Views/SourcingManager/Reports.cshtml` |
| `/SourcingManager/Clarifications` | `Views/SourcingManager/Clarifications.cshtml` |

## TopCommittee

| Route | View |
|---|---|
| `/TopCommittee/Dashboard` | `Views/TopCommittee/Dashboard.cshtml` |
| `/TopCommittee/Decisions` | `Views/TopCommittee/Decisions.cshtml` |
| `/TopCommittee/History` | `Views/TopCommittee/History.cshtml` |
| `/TopCommittee/Reports` | `Views/TopCommittee/Reports.cshtml` |
| `/TopCommittee/Clarifications` | `Views/TopCommittee/Clarifications.cshtml` |

## Supplier

| Route | View |
|---|---|
| `/Supplier/Dashboard` | `Views/Supplier/Dashboard.cshtml` |
| `/Supplier/Profile` | `Views/Supplier/Profile.cshtml` |
| `/Supplier/Invitations` | `Views/Supplier/Invitations.cshtml` |
| `/Supplier/Offers` | `Views/Supplier/Offers.cshtml` |
| `/Supplier/Clarifications` | `Views/Supplier/Clarifications.cshtml` |
| `/Supplier/History` | `Views/Supplier/History.cshtml` |

## Shared and account routes

Public: Home/Index, Home/About, Home/HowItWorks, Home/Contact.

Account: Login, Register, RegistrationStatus, EditRegistration, AccessDenied, GoToDashboard. Logout is POST only.

Admin/CreateUser, Admin/EditUser, Admin/CreateDepartment, Admin/EditDepartment, and Admin/ReviewSupplier are POST endpoints attached to forms on their parent pages. No separate create/edit pages exist.

Notifications are an account-specific navbar bell on every signed-in page, not a standalone route.

Admin has no clarification/report routes. Department has no report routes. SourcingOfficer/Award has been removed; the chosen offer belongs in Tender Details.

The default /<Role> URL redirects to that role's dashboard. Removed child-page URLs intentionally return 404; navigate through the parent workspace instead.

Supplier History contains only that supplier's submitted offers and personal summary downloads. My Offers handles active offer submission/editing. Suppliers do not send evaluation reports.
