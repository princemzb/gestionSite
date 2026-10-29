// Écran Sites et affiche (US-08) : copier le lien public, imprimer l'affiche. Fichier servi par l'application
// (politique de sécurité du contenu : aucun script en ligne).
document.addEventListener("click", (evenement) => {
  const copier = evenement.target.closest("[data-copier]");
  if (copier) {
    const libelle = copier.textContent;
    navigator.clipboard.writeText(copier.dataset.copier)
      .then(() => { copier.textContent = "Lien copié"; })
      .catch(() => { copier.textContent = "Copie impossible : sélectionnez le lien"; })
      .finally(() => { setTimeout(() => { copier.textContent = libelle; }, 2000); });
  }

  if (evenement.target.closest("[data-imprimer]")) {
    window.print();
  }
});
