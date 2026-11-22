(() => {
    'use strict';
    const dialog = document.getElementById('action-confirmation');
    if (!dialog) return;
    let waiting = null;
    const proceed = dialog.querySelector('[data-action-proceed]');
    const cancel = dialog.querySelector('[data-action-cancel]');
    const replay = new WeakSet();
    const value = (form, name, button) => button?.name?.toLowerCase() === name.toLowerCase() ? button.value
        : Array.from(form.elements).find(e => e.name?.toLowerCase() === name.toLowerCase() && !e.disabled &&
            !['submit', 'button', 'reset', 'image'].includes(e.type) && (!['radio', 'checkbox'].includes(e.type) || e.checked))?.value;
    function ask({title, description, confirm = 'Confirm', destructive = false, approval = false, intent = ''}) {
        if (waiting) return Promise.resolve(false);
        const trigger = document.activeElement;
        dialog.querySelector('h2').textContent = title;
        dialog.querySelector('p').textContent = description;
        proceed.textContent = confirm;
        dialog.dataset.destructive = String(destructive);
        dialog.dataset.approval = String(approval);
        dialog.dataset.intent = intent;
        return new Promise(resolve => { waiting = {resolve, trigger}; dialog.showModal(); });
    }
    function finish(accepted) { if (!waiting) return; const pending = waiting; waiting = null; dialog.close(); if (pending.trigger?.isConnected) pending.trigger.focus({preventScroll:true}); pending.resolve(accepted); }
    proceed.addEventListener('click', () => finish(true));
    cancel.addEventListener('click', () => finish(false));
    dialog.addEventListener('cancel', event => { event.preventDefault(); finish(false); });
    window.TiryaqConfirm = {ask};
    const describe = (title, description, confirm, destructive = false) => ({title, description, confirm, destructive});
    const approve = (title, description, confirm) => ({title, description, confirm, approval: true});
    function policy(form, button) {
        if ((button?.formMethod || form.method).toLowerCase() !== 'post') return null;
        const url = new URL(button?.hasAttribute('formaction') ? button.formAction : form.action, location.href);
        if (url.origin !== location.origin) return null;
        const [controller, action] = url.pathname.toLowerCase().split('/').filter(Boolean);
        const decision = value(form, 'decision', button);
        // The report dialog already has a separate consequence and confirmation step.
        if (action === 'reviewcommitteereport' && form.matches('.rv-confirm') && form.closest('#report-review-modal')) return null;
        // A reason dialog already presents the consequence and an explicit final submit action.
        if (form.closest('.modal') && form.querySelector('textarea[name="reason"][required]') &&
            (['returnrequest', 'rejectrequest', 'rejectoffer'].includes(action) || (action === 'reviewcommitteereport' && decision !== 'Approve'))) return null;
        if (controller === 'sourcingofficer') {
            switch (action) {
                case 'approverequest': return approve('Approve this request?', 'This records your approval. You will stay on the request page and can prepare its tender when ready.', 'Approve request');
                case 'returnrequest': return describe('Request changes?', 'Your reason will be sent to the department so it can update and resubmit this request.', 'Request changes');
                case 'rejectrequest': return describe('Reject this request?', 'The department will see your rejection and reason. This request will not proceed to tender preparation.', 'Reject request', true);
                case 'create': return approve('Publish and invite suppliers?', 'This publishes the tender and invites the selected suppliers to submit offers.', 'Publish & invite');
                case 'edit': return null; // Draft edits are reversible and already have an explicit Save action.
                case 'publishtender': return describe('Publish this tender?', 'The tender will become available for supplier invitations.', 'Publish tender');
                case 'publishandinvite': case 'createinvitation': return describe('Send supplier invitations?', 'This publishes the tender if needed and invites the selected approved suppliers. Existing invitations are kept.', 'Send invitations');
                case 'reviewandsend': case 'sendtoevaluation': return describe('Send this tender to the committee?', 'Your compliance decisions will be saved and the four-member committee will be prepared for evaluation. Further supplier submissions will stop.', 'Send to committee');
                case 'reviewcompliance': return form.hasAttribute('data-compliance-confirm') ? null : describe(
                    decision === 'Compliant' ? 'Mark this offer as compliant?' : 'Mark this offer as non-compliant?',
                    (decision === 'Compliant' ? 'This offer will be eligible for committee review.' : 'This offer will be excluded from committee review.') + ' This decision is final and cannot be changed.',
                    decision === 'Compliant' ? 'Confirm compliant' : 'Confirm non-compliant', decision !== 'Compliant');
                case 'closesubmissionsearly': return describe('Close submissions and send to committee?', 'Suppliers will no longer be able to submit or withdraw offers. The compliant offers will be sent to the evaluation committee.', 'Close submissions and send');
                case 'reviewcommitteereport': return decision === 'Approve'
                    ? approve('Approve and forward this report?', 'The committee report and chosen offer will be forwarded unchanged to the sourcing manager.', 'Approve & forward')
                    : describe(decision === 'Reject' ? 'Reject this report?' : 'Return this report for edits?', decision === 'Reject' ? 'This cancels the tender and records your reason.' : 'The committee will need to revise its recommendation and collect four fresh approvals.', 'Confirm decision', decision === 'Reject');
            }
        }
        if (controller === 'department' && ['create', 'edit'].includes(action)) return describe(action === 'edit' ? 'Resubmit this request?' : 'Submit this purchase request?', 'The sourcing officer will receive the requirements and items you entered for review.', action === 'edit' ? 'Resubmit request' : 'Submit request');
        if (controller === 'evaluationcommittee') {
            switch (action) {
                case 'start': return describe('Start this evaluation?', 'The four committee positions and shared evaluation weights will be initialized.', 'Start evaluation');
                case 'selectoffer': return describe('Choose this offer?', 'This becomes the committee-selected offer. Changing the selection clears earlier approvals so all four members can review it again.', 'Choose offer');
                case 'saveweights': return describe('Change the shared percentages?', 'The new financial and technical weights apply to all offers. Previous approvals will be cleared.', 'Save percentages');
                case 'approve': return approve('Record your approval?', 'Your approval covers the chosen offer and agreed percentages. After all four members approve, the final report can be generated.', 'Approve evaluation');
                case 'rejectoffer': return describe('Reject and return for review?', 'Your reason will be recorded and the committee will need to reconsider the selected offer.', 'Reject offer', true);
                case 'recommendbids': return value(form, 'writeReport', button) === 'true' ? describe('Generate and send the final report?', 'The AI will write the report for the committee’s chosen offer and agreed percentages. Once generated successfully, it will be sent to the sourcing officer.', 'Generate & send') : null;
            }
        }
        if (controller === 'supplier') {
            if (action === 'canceloffer') return describe('Cancel this offer?', 'Your submitted offer will no longer be considered for this tender.', 'Cancel offer', true);
            if (action === 'saveoffer' && value(form, 'submissionCommand', button) === 'Submit') return describe('Submit this offer?', 'Your pricing, technical response and documents will be submitted to the sourcing officer for review.', 'Submit offer');
            if (action === 'respond') return value(form, 'response', button) === 'Accepted'
                ? describe('Accept this invitation?', 'You can then prepare and submit an offer for this tender.', 'Accept invitation')
                : describe('Decline this invitation?', 'Your company will be recorded as declining this invitation.', 'Decline invitation', true);
        }
        if (controller === 'sourcingmanager') {
            if (action === 'forward') return describe('Refer to the CEO & Chiefs?', 'The report, chosen offer and referral reason will be sent to the CEO & Chiefs for its decision.', 'Forward to committee');
            if (action === 'decide') {
                const choice = value(form, 'Decision', button);
                return choice === 'Approved' ? approve('Approve this recommendation?', 'This records your approval. You can then add the referral reason and forward the case to the CEO & Chiefs.', 'Approve recommendation')
                    : describe(choice === 'Rejected' ? 'Reject this recommendation?' : 'Return for review?', choice === 'Rejected' ? 'This rejects the recommendation and records your comments.' : 'Your comments will be returned for review of the recommendation.', choice === 'Rejected' ? 'Reject recommendation' : 'Return for review', choice === 'Rejected');
            }
        }
        // The rejection reason dialog already explains and confirms this final decision.
        if (controller === 'topcommittee' && action === 'submitvote' && value(form, 'Vote.Decision', button) === 'Rejected' && form.closest('.modal')) return null;
        if (controller === 'topcommittee' && action === 'submitvote') return approve('Record your final approval?', 'Your vote and notes will be saved and your voting form will be locked. The final required approval may award the tender to the nominated supplier.', 'Record approval');
        if ((controller === 'clarification' && action === 'update' && value(form, 'resolve', button) === 'true') || (controller === 'topcommittee' && action === 'resolveclarification')) return describe('Confirm this question is resolved?', 'Your confirmation will be recorded. The conversation closes once both participants confirm resolution.', 'Confirm resolution');
        if ((controller === 'clarification' && action === 'create') || action === 'createclarification') return describe('Send this clarification?', 'Your question will be sent to the selected participant and linked to this record. An open question may pause related approvals until resolved.', 'Send question');
        if (controller === 'admin') {
            switch (action) {
                case 'createuser': return describe('Create this account?', 'The user will receive the selected role and workspace permissions.', 'Create account');
                case 'edituser': { const disabling = form.dataset.confirmDisable === 'true' && !form.querySelector('[name="IsActive"][type="checkbox"]')?.checked;
                    return describe(disabling ? 'Disable this account?' : 'Update account access?', disabling ? 'This user will lose access to the system.' : 'The selected account details and role permissions will be saved.', disabling ? 'Disable account' : 'Save account', disabling); }
                case 'createdepartment': case 'editdepartment': return describe(action === 'createdepartment' ? 'Create this department?' : 'Update this department?', 'The department details and availability will be saved.', 'Save department');
                case 'reviewsupplier': {
                    const status = value(form, 'Review.Status', button);
                    if (status === 'Approved') return approve('Approve this supplier?', 'The supplier’s application will be approved and the review reason will be recorded.', 'Approve supplier');
                    if (status === 'Rejected') return describe('Reject this supplier application?', 'The application will be rejected and the review reason will be recorded.', 'Reject supplier', true);
                    return describe('Save this supplier decision?', 'The supplier’s application status and your review reason will be updated. Approval controls whether the company can participate in tenders.', 'Save decision');
                }
            }
        }
        if (controller === 'account' && ['register', 'editregistration'].includes(action)) return {...describe('Submit supplier application?', `${value(form, 'CompanyName', button) || 'Your company'}\nThe admin team will review your details and documents. Supplier tools become available after approval.`, 'Submit application'), intent:'registration'};
        return null;
    }
    document.addEventListener('submit', async event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) return;
        if (replay.has(form)) { replay.delete(form); return; }
        const button = event.submitter;
        const details = policy(form, button);
        if (!details) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        if (typeof form.tiryaqValidate === "function" && !form.tiryaqValidate()) return;
        if (!form.reportValidity()) return;
        // Respect existing unobtrusive validation before presenting a decision.
        if (window.jQuery?.validator && !window.jQuery(form).valid()) return;
        if (!await ask(details) || !form.isConnected || button?.disabled) return;
        replay.add(form);
        try { form.requestSubmit(button || undefined); } finally { replay.delete(form); }
    }, true);
    document.querySelectorAll('.alert-success, .ev-notice-success').forEach(message => { message.setAttribute('role', 'status'); });
})();
