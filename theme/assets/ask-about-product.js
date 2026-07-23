document.addEventListener('DOMContentLoaded', () => {
  document.querySelectorAll('[data-product-ask]').forEach((container) => {
    const productId = container.dataset.productId;
    const input = container.querySelector('[data-product-ask-input]');
    const button = container.querySelector('[data-product-ask-submit]');
    const spinner = container.querySelector('[data-product-ask-spinner]');
    const results = container.querySelector('[data-product-ask-results]');
    const errorEl = container.querySelector('[data-product-ask-error]');
    const errorText = container.querySelector('[data-product-ask-error-text]');
    const withoutEl = container.querySelector('[data-product-ask-answer-without]');
    const withEl = container.querySelector('[data-product-ask-answer-with]');
    const chunksEl = container.querySelector('[data-product-ask-chunks]');

    const setLoading = (isLoading) => {
      button.disabled = isLoading;
      button.classList.toggle('loading', isLoading);
      spinner.classList.toggle('hidden', !isLoading);
    };

    const showError = (message) => {
      results.hidden = true;
      errorText.textContent = message;
      errorEl.hidden = false;
    };

    button.addEventListener('click', async () => {
      const question = input.value.trim();
      if (!question) return;

      errorEl.hidden = true;
      setLoading(true);

      try {
        const response = await fetch(`/apps/inventory/products/${productId}/ask`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ question }),
        });

        if (!response.ok) {
          showError('Something went wrong asking that question. Please try again.');
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
        showError('Something went wrong asking that question. Please check your connection and try again.');
      } finally {
        setLoading(false);
      }
    });
  });
});
