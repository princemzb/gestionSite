"""POC PREP-06 (expérimental) : prépare une organisation de démonstration sur l'API locale.

Usage : python3 scripts/poc/prep06_preparer.py <jeton_activation_admin> <email_admin>
Écrit sur la sortie des lignes CLE=valeur (organisation, site, deux techniciens et leurs jetons).
Base locale uniquement : comptes et mots de passe de démonstration, sans donnée réelle.
"""
import json
import sys
import time
import urllib.request
import uuid

# Cible fixe : l'API locale lancée par prep06.sh (aucune adresse fournie de l'extérieur).
API = "http://localhost:5280"
MOT_DE_PASSE = "mot-de-passe-de-demonstration"


def identifiant(valeur):
    """Identifiant reçu de l'API, vérifié avant d'entrer dans un chemin."""
    return str(uuid.UUID(str(valeur)))


def appeler(chemin, corps, jeton=None):
    requete = urllib.request.Request(API + chemin, data=json.dumps(corps).encode(), method="POST")
    requete.add_header("Content-Type", "application/json")
    if jeton:
        requete.add_header("Authorization", "Bearer " + jeton)
    with urllib.request.urlopen(requete, timeout=30) as reponse:
        contenu = reponse.read()
        return json.loads(contenu) if contenu else None


def connecter(email):
    return appeler("/auth/login", {"email": email, "motDePasse": MOT_DE_PASSE})["jetonAcces"]


def main(jeton_activation, email_admin):
    appeler("/auth/activate", {"jeton": jeton_activation, "motDePasse": MOT_DE_PASSE})
    admin = connecter(email_admin)
    with urllib.request.urlopen(urllib.request.Request(API + "/me", headers={"Authorization": "Bearer " + admin})) as reponse:
        moi = json.load(reponse)
    organisation = identifiant(moi["organisations"][0]["id"])
    site = identifiant(appeler(f"/organizations/{organisation}/sites", {"nom": "Site du POC"}, admin)["id"])
    sortie = {"PREP06_ORGANISATION": organisation, "PREP06_SITE": site}
    suffixe = str(int(time.time()))
    for nom in ("marc", "lea"):
        email = f"{nom}.{suffixe}@poc.test"
        cree = appeler(f"/organizations/{organisation}/members",
                       {"nom": nom, "email": email, "roles": [{"role": "Technicien", "siteId": site}]}, admin)
        appeler("/auth/activate", {"jeton": cree["activation"]["jeton"], "motDePasse": MOT_DE_PASSE})
        sortie[f"PREP06_{nom.upper()}"] = identifiant(cree["compteId"])
        sortie[f"PREP06_{nom.upper()}_JETON"] = connecter(email)
    for cle, valeur in sortie.items():
        print(f"{cle}={valeur}")


if __name__ == "__main__":
    main(*sys.argv[1:3])
