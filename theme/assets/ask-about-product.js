document.addEventListener('DOMContentLoaded', () => {
  document.querySelectorAll('[data-product-ask]').forEach((container) => {
    const productId = container.dataset.productId;
    const input = container.querySelector('[data-product-ask-input]');
    const button = container.querySelector('[data-product-ask-submit]');
    const results = container.querySelector('[data-product-ask-results]');
    const withoutEl = container.querySelector('[data-product-ask-answer-without]');
    const withEl = container.querySelector('[data-product-ask-answer-with]');
    const chunksEl = container.querySelector('[data-product-ask-chunks]');

    button.addEventListener('click', async () => {
      const question = input.value.trim();
      if (!question) return;

      button.disabled = true;
      button.textContent = 'Asking...';

      try {
        const response = await fetch(`/apps/inventory/products/${productId}/ask`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ question }),
        });

        if (!response.ok) {
          withoutEl.textContent = 'Something went wrong asking that question.';
          withEl.textContent = '';
          results.hidden = false;
          return;
        }

        const data = await response.json();
        withoutEl.textContent = data.answerWithoutContext;
        withEl.textContent = data.answerWithContext;
        chunksEl.innerHTML = '';
        (data.retrievedChunks || []).forEach((chunk) => {
          const li = document.createElement('li');
          li.textContent = chunk;
          chunksEl.appendChild(li);
        });
        results.hidden = false;
      } catch (error) {
        console.error('Product ask failed:', error);
      } finally {
        button.disabled = false;
        button.textContent = 'Ask';
      }
    });
  });
});
