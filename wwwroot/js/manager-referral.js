// Sourcing Manager: asks the AI to draft the referral text and shows the result.
document.querySelector('[data-draft-referral]')?.addEventListener('click', async event => {
    const button = event.currentTarget, form = button.form;
    const field = form.elements.Reason, feedback = form.querySelector('[data-referral-feedback]');
    if (field.value.trim() && !await window.TiryaqConfirm.ask({ title: 'Replace the referral reason?', description: 'The current text will be replaced with a newly generated AI draft. Review the draft before forwarding.', confirm: 'Replace with AI draft' })) return;
    const original = field.value;
    button.disabled = true;
    feedback.textContent = 'Generating summary…';
    try {
        const data = new FormData();
        data.set('reportId', form.elements.ReportId.value);
        data.set('__RequestVerificationToken', form.elements.__RequestVerificationToken.value);
        const response = await fetch(button.dataset.url, { method: 'POST', body: data });
        const result = await response.json();
        if (!response.ok) throw new Error(result.error || 'Could not generate the summary. Try again.');
        if (field.value !== original) throw new Error('Your text changed during generation. Kept your current text; generate again when ready.');
        field.value = result.summary;
        field.dispatchEvent(new Event('input', { bubbles: true }));
        feedback.textContent = 'AI draft ready. Check the facts and referral policy reason before forwarding.';
        field.focus();
    } catch (error) { feedback.textContent = error.message; }
    finally { button.disabled = false; }
});
