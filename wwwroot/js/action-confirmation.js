(() => {
    'use strict';
    const dialog = document.getElementById('action-confirmation');
    if (!dialog) return;
    let waiting = null;
    const proceed = dialog.querySelector('[data-action-proceed]');
    const cancel = dialog.querySelector('[data-action-cancel]');
    const replay = new WeakSet();
    const value = (form, name, button) => button?.name?.toLowerCase() === name.toLowerCase() ? button.value
        : Array.from(form.elements).find(e => e.name?.toLowerCase() === name.toLowerCase())?.value;
    function ask({title, description, confirm = 'Confirm', destructive = false}) {
        if (waiting) return Promise.resolve(false);
        const trigger = document.activeElement;
        dialog.querySelector('h2').textContent = title;
        dialog.querySelector('p').textContent = description;
        proceed.textContent = confirm;
        dialog.dataset.destructive = String(destructive);
        return new Promise(resolve => { waiting = {resolve, trigger}; dialog.showModal(); });
    }
    function finish(accepted) { if (!waiting) return; const pending = waiting; waiting = null; dialog.close(); if (pending.trigger?.isConnected) pending.trigger.focus({preventScroll:true}); pending.resolve(accepted); }
    proceed.addEventListener('click', () => finish(true));
    cancel.addEventListener('click', () => finish(false));
    dialog.addEventListener('cancel', event => { event.preventDefault(); finish(false); });
    window.TiryaqConfirm = {ask};
    const describe = (title, description, confirm, destructive = false) => ({title, description, confirm, destructive});
    function policy(form, button) {
        if ((button?.formMethod || form.method).toLowerCase() !== 'post') return null;
        const url = new URL(button?.hasAttribute('formaction') ? button.formAction : form.action, location.href);
        if (url.origin !== location.origin) return null;
        const [controller, action] = url.pathname.toLowerCase().split('/').filter(Boolean);
        const decision = value(form, 'decision', button);
        // A reason dialog already presents the consequence and an explicit final submit action.
        if (form.closest('.modal') && form.querySelector('textarea[name="reason"][required]') &&
            (['returnrequest', 'rejectrequest', 'rejectoffer'].includes(action) || (action === 'reviewcommitteereport' && decision !== 'Approve'))) return null;
        if (controller === 'sourcingofficer') {
            switch (action) {
                case 'approverequest': return describe('Approve this request?', 'This records your approval. You will stay on the request page and can prepare its tender when ready.', 'Approve request');
                case 'returnrequest': return describe('Request changes?', 'Your reason will be sent to the department so it can update and resubmit this request.', 'Request changes');
                case 'rejectrequest': return describe('Reject this request?', 'The department will see your rejection and reason. This request will not proceed to tender preparation.', 'Reject request', true);
                case 'create': return value(form, 'launch', button) === 'true'
                    ? describe('Approve, publish and invite?', 'This approves the purchase request, publishes its tender, and invites the selected suppliers to submit offers.', 'Publish & invite')
                    : describe('Approve and save a draft?', 'This records approval of the request and creates a draft tender. Suppliers will not be invited yet.', 'Approve & save draft');
                case 'edit': return describe('Save tender changes?', 'The updated title, requirements, terms and submission dates will replace the saved tender details.', 'Save changes');
                case 'publishtender': return describe('Publish this tender?', 'The tender will become available for supplier invitations.', 'Publish tender');
                case 'publishandinvite': case 'createinvitation': return describe('Send supplier invitations?', 'This publishes the tender if needed and invites the selected approved suppliers. Existing invitations are kept.', 'Send invitations');
                case 'reviewandsend': case 'sendtoevaluation': return describe('Send this tender to the committee?', 'Your compliance decisions will be saved and the four-member committee will be prepared for evaluation. Further supplier submissions will stop.', 'Send to committee');
                case 'reviewcompliance': return describe('Save compliance decision?', 'This records the offer’s final compliance decision for committee eligibility.', 'Save decision');
                case 'reviewcommitteereport': return decision === 'Approve'
                    ? describe('Approve and forward this report?', 'The committee report and chosen offer will be forwarded unchanged to the sourcing manager.', 'Approve & forward')
                    : describe(decision === 'Reject' ? 'Reject this report?' : 'Return this report for edits?', decision === 'Reject' ? 'This cancels the tender and records your reason.' : 'The committee will need to revise its recommendation and collect four fresh approvals.', 'Confirm decision', decision === 'Reject');
            }
        }
        if (controller === 'department' && ['create', 'edit'].includes(action)) return describe(action === 'edit' ? 'Resubmit this request?' : 'Submit this purchase request?', 'The sourcing officer will receive the requirements and items you entered for review.', action === 'edit' ? 'Resubmit request' : 'Submit request');
        if (controller === 'evaluationcommittee') {
            switch (action) {
                case 'start': return describe('Start this evaluation?', 'The four committee positions and shared evaluation weights will be initialized.', 'Start evaluation');
                case 'selectoffer': return describe('Choose this offer?', 'This becomes the committee-selected offer. Changing the selection clears earlier approvals so all four members can review it again.', 'Choose offer');
                case 'saveweights': return describe('Change the shared percentages?', 'The new financial and technical weights apply to all offers. Previous approvals will be cleared.', 'Save percentages');
                case 'approve': return describe('Record your approval?', 'Your approval covers the chosen offer and agreed percentages. After all four members approve, the final report can be generated.', 'Approve evaluation');
                case 'rejectoffer': return describe('Reject and return for review?', 'Your reason will be recorded and the committee will need to reconsider the selected offer.', 'Reject offer', true);
                case 'recommendbids': return value(form, 'writeReport', button) === 'true' ? describe('Generate and send the final report?', 'The AI will write the report for the committee’s chosen offer and agreed percentages. Once generated successfully, it will be sent to the sourcing officer.', 'Generate & send') : null;
            }
        }
        if (controller === 'supplier') {
            if (action === 'saveoffer' && value(form, 'submissionCommand', button) === 'Submit') return describe('Submit this offer?', 'Your pricing, technical response and documents will be submitted to the sourcing officer for review.', 'Submit offer');
            if (action === 'respond') return value(form, 'response', button) === 'Accepted'
                ? describe('Accept this invitation?', 'You can then prepare and submit an offer for this tender.', 'Accept invitation')
                : describe('Decline this invitation?', 'Your company will be recorded as declining this invitation.', 'Decline invitation', true);
        }
        if (controller === 'sourcingmanager') {
            if (action === 'forward') return describe('Refer to the CEO & Chiefs?', 'The report, chosen offer and referral reason will be sent to the CEO & Chiefs for its decision.', 'Forward to committee');
            if (action === 'decide') {
                const choice = value(form, 'Decision', button);
                return choice === 'Approved' ? describe('Approve this recommendation?', 'Your approval will be recorded and the tender will advance to the next decision stage required by its referral policy.', 'Approve recommendation')
                    : describe(choice === 'Rejected' ? 'Reject this recommendation?' : 'Return for review?', choice === 'Rejected' ? 'This rejects the recommendation and records your comments.' : 'Your comments will be returned for review of the recommendation.', choice === 'Rejected' ? 'Reject recommendation' : 'Return for review', choice === 'Rejected');
            }
        }
        if (controller === 'topcommittee' && action === 'submitvote') return describe('Record your final approval?', 'Your vote and notes will be saved and your voting form will be locked. The final required approval may award the tender to the nominated supplier.', 'Record approval');
        if ((controller === 'clarification' && action === 'update' && value(form, 'resolve', button) === 'true') || (controller === 'topcommittee' && action === 'resolveclarification')) return describe('Confirm this question is resolved?', 'Your confirmation will be recorded. The conversation closes once both participants confirm resolution.', 'Confirm resolution');
        if ((controller === 'clarification' && action === 'create') || action === 'createclarification') return describe('Send this clarification?', 'Your question will be sent to the selected participant and linked to this record. An open question may pause related approvals until resolved.', 'Send question');
        if (controller === 'admin') {
            switch (action) {
                case 'createuser': return describe('Create this account?', 'The user will receive the selected role and workspace permissions.', 'Create account');
                case 'edituser': { const disabling = form.dataset.confirmDisable === 'true' && !form.querySelector('[name="IsActive"][type="checkbox"]')?.checked;
                    return describe(disabling ? 'Disable this account?' : 'Update account access?', disabling ? 'This user will lose access to the system.' : 'The selected account details and role permissions will be saved.', disabling ? 'Disable account' : 'Save account', disabling); }
                case 'createdepartment': case 'editdepartment': return describe(action === 'createdepartment' ? 'Create this department?' : 'Update this department?', 'The department details and availability will be saved.', 'Save department');
                case 'reviewsupplier': return describe('Save this supplier decision?', 'The supplier’s application status and your review reason will be updated. Approval controls whether the company can participate in tenders.', 'Save decision');
            }
        }
        if (controller === 'account' && ['register', 'editregistration'].includes(action)) return describe('Submit your supplier application?', 'Your company details and supporting documents will be sent to administration for review.', 'Submit application');
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
        if (!form.reportValidity()) return;
        // Respect existing unobtrusive validation before presenting a decision.
        if (window.jQuery?.validator && !window.jQuery(form).valid()) return;
        if (!await ask(details) || !form.isConnected || button?.disabled) return;
        replay.add(form);
        try { form.requestSubmit(button || undefined); } finally { replay.delete(form); }
    }, true);
    document.querySelectorAll('.alert-success, .ev-notice-success').forEach(message => { message.setAttribute('role', 'status'); });
})();
