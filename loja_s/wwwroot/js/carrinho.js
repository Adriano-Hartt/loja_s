// Carrinho: ao mudar a quantidade (digitando, ou com − e +) o preço é atualizado sozinho, sem botão.
(() => {
    const brl = v => Number(v).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const setText = (selector, value) => {
        const el = document.querySelector(selector);
        if (el) el.textContent = brl(value);
    };

    document.querySelectorAll(".cart-item").forEach(item => {
        const form = item.querySelector(".cart-qty-form");
        const input = form && form.querySelector('input[name="quantidade"]');
        const erro = form && form.querySelector(".qty-error");
        if (!form || !input) return;

        let ultima = parseInt(input.value, 10) || 1;
        let timer = null;

        const enviar = async () => {
            let qtd = parseInt(input.value, 10);
            if (!Number.isFinite(qtd) || qtd < 1) { qtd = 1; input.value = "1"; }
            if (qtd === ultima) return;

            form.classList.add("is-loading");
            erro.hidden = true;
            try {
                const resposta = await fetch("/Carrinho/AtualizarQuantidadeAjax", {
                    method: "POST",
                    body: new FormData(form),
                    credentials: "same-origin"
                });
                const dados = await resposta.json();

                if (dados.removido) { window.location.reload(); return; }

                input.value = dados.quantidade;
                ultima = dados.quantidade;
                item.querySelector("[data-item-subtotal]").textContent = brl(dados.subtotalItem);
                setText("[data-sum-subtotal]", dados.subtotal);
                setText("[data-sum-frete]", dados.frete);
                setText("[data-sum-total]", dados.total);

                if (!resposta.ok && dados.erro) {
                    erro.textContent = dados.erro;
                    erro.hidden = false;
                }
            } catch {
                input.value = ultima;
                erro.textContent = "Não foi possível atualizar agora. Tente novamente.";
                erro.hidden = false;
            } finally {
                form.classList.remove("is-loading");
            }
        };

        const agendar = () => { clearTimeout(timer); timer = setTimeout(enviar, 450); };

        input.addEventListener("input", agendar);
        input.addEventListener("change", () => { clearTimeout(timer); enviar(); });
        form.addEventListener("submit", e => { e.preventDefault(); clearTimeout(timer); enviar(); });
        form.querySelectorAll(".qty-step").forEach(btn => btn.addEventListener("click", () => {
            const novo = Math.max(1, (parseInt(input.value, 10) || 1) + parseInt(btn.dataset.step, 10));
            input.value = novo;
            agendar();
        }));
    });
})();
