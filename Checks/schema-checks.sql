-- Read schema metadata and test constraints using temporary rows. Every fixture is rolled back.
USE SourcingTenderManagementCodeFirst;
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT OFF;
IF (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0 AND name <> N'__EFMigrationsHistory') <> 28
    THROW 51000, 'Expected 28 tables.', 1;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1)
    THROW 51000, 'A foreign key is disabled or untrusted.', 1;
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE is_disabled = 1 OR is_not_trusted = 1)
    THROW 51000, 'A check constraint is disabled or untrusted.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @u1 nvarchar(450) = CONVERT(nvarchar(36),NEWID()), @u2 nvarchar(450) = CONVERT(nvarchar(36),NEWID());
    INSERT AspNetUsers(Id,FullName,EmailConfirmed,PhoneNumberConfirmed,TwoFactorEnabled,LockoutEnabled,AccessFailedCount)
        VALUES (@u1,N'Temporary check',0,0,0,1,0), (@u2,N'Temporary check',0,0,0,1,0);
    INSERT Departments(Name) VALUES (CONCAT(N'Constraint check ',NEWID()));
    DECLARE @d int = SCOPE_IDENTITY();
    INSERT PurchaseRequests(DepartmentId,RequestedByUserId,Title,Reason,Priority,TargetDate)
        VALUES (@d,@u1,N'Constraint check',N'Temporary verification',N'Normal','2030-01-01');
    DECLARE @r int = SCOPE_IDENTITY();

    BEGIN TRY
        INSERT PurchaseRequestItems(PurchaseRequestId,ItemName,Quantity,Unit,Specifications) VALUES (@r,N'Invalid',-1,N'Each',N'Test');
        THROW 51000, 'Negative quantity was accepted.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 547 THROW;
    END CATCH;

    INSERT Tenders(PurchaseRequestId,CreatedByUserId,Title,Description,OpensAt,ClosesAt)
        VALUES (@r,@u1,N'First',N'Test','2030-01-01','2030-02-01');
    DECLARE @t1 int = SCOPE_IDENTITY();
    INSERT Tenders(PurchaseRequestId,CreatedByUserId,Title,Description,OpensAt,ClosesAt)
        VALUES (@r,@u1,N'Second',N'Test','2030-01-01','2030-02-01');
    DECLARE @t2 int = SCOPE_IDENTITY();
    INSERT SupplierProfiles(UserId,CompanyName,RegistrationNumber,ContactName)
        VALUES (@u2,N'Temporary check',CONVERT(nvarchar(36),NEWID()),N'Check');
    DECLARE @s int = SCOPE_IDENTITY();
    INSERT Invitations(TenderId,SupplierProfileId,InvitedByUserId) VALUES (@t1,@s,@u1);
    DECLARE @i int = SCOPE_IDENTITY();

    BEGIN TRY
        INSERT Bids(InvitationId,TenderId,Currency,DeliveryDays,TaxAmount,AdditionalCosts) VALUES (@i,@t2,'USD',10,0,0);
        THROW 51000, 'An offer was accepted against a different tender invitation.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 547 THROW;
    END CATCH;

    INSERT CommitteeAssignments(TenderId,UserId,CommitteeType,Position) VALUES (@t1,@u1,'Top',1);
    DECLARE @a1 int = SCOPE_IDENTITY();
    BEGIN TRY
        INSERT CommitteeAssignments(TenderId,UserId,CommitteeType,Position) VALUES (@t1,@u2,'Top',1);
        THROW 51000, 'Duplicate committee position was accepted.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2601,2627) THROW;
    END CATCH;
    INSERT CommitteeAssignments(TenderId,UserId,CommitteeType,Position) VALUES (@t1,@u2,'Top',2);
    DECLARE @a2 int = SCOPE_IDENTITY();

    BEGIN TRY
        INSERT TenderApprovals(TenderId,UserId,CommitteeAssignmentId,Stage,Decision) VALUES (@t1,@u2,@a1,'Top','Approved');
        THROW 51000, 'A different user approved the assigned member position.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 547 THROW;
    END CATCH;

    INSERT TenderApprovals(TenderId,UserId,CommitteeAssignmentId,Stage,Decision) VALUES (@t1,@u1,@a1,'Top','Approved');
    BEGIN TRY
        INSERT TenderApprovals(TenderId,UserId,CommitteeAssignmentId,Stage,Decision) VALUES (@t1,@u1,@a1,'Top','Approved');
        THROW 51000, 'The same member approved twice.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (2601,2627) THROW;
    END CATCH;

    BEGIN TRY
        INSERT TenderApprovals(TenderId,UserId,CommitteeAssignmentId,Stage,Decision) VALUES (@t1,@u2,@a2,'Top','ReturnedForReview');
        THROW 51000, 'A committee rejection/return decision was stored.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 547 THROW;
    END CATCH;

    BEGIN TRY
        UPDATE Tenders SET Status='Awarded' WHERE TenderId=@t1;
        THROW 51000, 'Tender awarded without a winning bid.', 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() <> 547 THROW;
    END CATCH;
    INSERT Bids(InvitationId,TenderId,Currency,DeliveryDays,TaxAmount,AdditionalCosts) VALUES (@i,@t1,'USD',10,0,0);
    DECLARE @chosenBid int = SCOPE_IDENTITY();
    INSERT EvaluationReports(TenderId,RecommendedBidId,ManagerSummary,Status)
        VALUES (@t1,@chosenBid,N'Manager summary is separate from the officer summary.','Approved');
    IF NOT EXISTS (SELECT 1 FROM EvaluationReports WHERE TenderId=@t1 AND Status='Approved' AND ManagerSummary IS NOT NULL AND OfficerSummary IS NULL)
        THROW 51000, 'Shared final approval status or separate manager summary failed.', 1;
    INSERT TenderApprovals(TenderId,UserId,Stage,Decision,Notes)
        VALUES (@t1,@u1,'Manager','Rejected',N'Manager rejection is allowed; committee rejection remains disallowed.');
    ROLLBACK TRANSACTION;
    PRINT 'PASS: 28 tables, trusted constraints, positive quantities, tender/invitation consistency, committee position uniqueness, assigned-member approval, duplicate-vote prevention, design-only rejection, and award requirements. Fixtures rolled back.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
