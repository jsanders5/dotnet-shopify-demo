// This theme's own CSS sets `display: flex` unconditionally on
// .product__inventory (the class this badge reuses for visual
// consistency), which overrides the `hidden` attribute regardless of
// selector specificity. Toggling el.style.display directly wins over
// that, since inline styles always beat stylesheet rules.
function setVisible(el, visible) {
  el.style.display = visible ? '' : 'none';
}

async function checkLowStock(el, variantId) {
  if (!variantId) {
    setVisible(el, false);
    return;
  }

  try {
    const response = await fetch(`/apps/inventory/products/by-variant/${variantId}`);
    if (!response.ok) {
      setVisible(el, false);
      return;
    }

    const data = await response.json();
    setVisible(el, data.isLowStock);
  } catch (error) {
    console.error('Low stock alert check failed:', error);
    setVisible(el, false);
  }
}

document.addEventListener('DOMContentLoaded', () => {
  document.querySelectorAll('[data-low-stock-alert]').forEach((el) => {
    checkLowStock(el, el.dataset.variantId);
  });

  // Most Online Store 2.0 themes swap the selected variant client-side,
  // without a full page reload, so the badge needs to react rather than
  // only check once on load. Two conventions cover this across themes:
  // the classic `<input name="id">`/`<select name="id">` variant field
  // (still present here as a hidden field even though this theme's swatch
  // picker doesn't fire `change` on it), and the `variant:change`
  // CustomEvent modern themes (including this one) dispatch instead.
  document.addEventListener('change', (event) => {
    const target = event.target;
    if (!(target instanceof HTMLElement)) return;
    if (target.name !== 'id') return;

    document.querySelectorAll('[data-low-stock-alert]').forEach((el) => {
      checkLowStock(el, target.value);
    });
  });

  document.addEventListener('variant:change', (event) => {
    const variantId = event.detail?.variant?.id ?? event.detail?.id;
    if (!variantId) return;

    document.querySelectorAll('[data-low-stock-alert]').forEach((el) => {
      checkLowStock(el, variantId);
    });
  });
});
