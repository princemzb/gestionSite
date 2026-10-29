// Écran Sites et affiche (US-08) : copier le lien public, imprimer l'affiche. Fichier servi par l'application
// (politique de sécurité du contenu : aucun script en ligne).
document.addEventListener("click", (evenement) => {
  const copier = evenement.target.closest("[data-copier]");
  if (copier) {
    navigator.clipboard.writeText(copier.dataset.copier).then(() => {
      const libelle = copier.textContent;
      copier.textContent = "Lien copié";
      setTimeout(() => { copier.textContent = libelle; }, 2000);
    });
  }

  if (evenement.target.closest("[data-imprimer]")) {
    window.print();
  }
});
