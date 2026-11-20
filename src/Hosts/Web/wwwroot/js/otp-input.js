// One-time code boxes on the sign-in page (LoginPage.razor): one digit per box. Typing moves on to the next box,
// Backspace in an empty box goes back, the arrow keys move between boxes, and a pasted or autofilled code fills every
// box. The digits are copied into the hidden field named by data-target, which is the value the page reads, and a
// complete code submits the form. Listeners sit on the document, so boxes the page draws again keep working.
(function () {
    'use strict';

    const selector = '.otp-digit';

    function digitBox(target) {
        return target instanceof HTMLInputElement && target.matches(selector) ? target : null;
    }

    function boxesOf(box) {
        return Array.from(box.closest('.otp-boxes').querySelectorAll(selector));
    }

    // Copies the digits into the field the page reads (a change event, like typing in it), then submits a full code.
    function sync(boxes, submitWhenComplete) {
        const group = boxes[0].closest('.otp-boxes');
        const field = document.getElementById(group.dataset.target);
        if (!field) {
            return;
        }
        const code = boxes.map(box => box.value).join('');
        field.value = code;
        field.dispatchEvent(new Event('change', { bubbles: true }));
        if (submitWhenComplete && code.length === boxes.length && boxes.every(box => box.value.length === 1)) {
            field.form?.requestSubmit();
        }
    }

    // Puts the digits in the boxes from `start` on, and the cursor in the box after the last one filled.
    function fill(boxes, start, digits) {
        let index = start;
        for (const digit of digits) {
            if (index >= boxes.length) {
                break;
            }
            boxes[index++].value = digit;
        }
        boxes[Math.min(index, boxes.length - 1)].focus();
    }

    document.addEventListener('input', event => {
        const box = digitBox(event.target);
        if (!box) {
            return;
        }
        const boxes = boxesOf(box);
        const digits = box.value.replace(/\D/g, '');
        box.value = '';
        // One digit when typing; the whole code when the phone fills it in from a message.
        if (digits) {
            fill(boxes, boxes.indexOf(box), digits);
        }
        sync(boxes, digits.length > 0);
    });

    document.addEventListener('keydown', event => {
        const box = digitBox(event.target);
        if (!box) {
            return;
        }
        const boxes = boxesOf(box);
        const index = boxes.indexOf(box);
        if (event.key === 'Backspace' && !box.value && index > 0) {
            event.preventDefault();
            boxes[index - 1].value = '';
            boxes[index - 1].focus();
            sync(boxes, false);
        } else if (event.key === 'ArrowLeft' && index > 0) {
            event.preventDefault();
            boxes[index - 1].focus();
        } else if (event.key === 'ArrowRight' && index < boxes.length - 1) {
            event.preventDefault();
            boxes[index + 1].focus();
        }
    });

    document.addEventListener('paste', event => {
        const box = digitBox(event.target);
        const digits = (event.clipboardData?.getData('text') ?? '').replace(/\D/g, '');
        if (!box || !digits) {
            return;
        }
        event.preventDefault();
        const boxes = boxesOf(box);
        // A whole code starts at the first box, wherever it was pasted.
        fill(boxes, digits.length >= boxes.length ? 0 : boxes.indexOf(box), digits);
        sync(boxes, true);
    });

    // Entering a box selects its digit, so typing replaces it.
    document.addEventListener('focusin', event => {
        digitBox(event.target)?.select();
    });
})();
