"""POC PREP-06 (expérimental) : prépare une organisation de démonstration sur l'API locale.

Usage : python3 scripts/poc/prep06_preparer.py <url_api> <jeton_activation_admin> <email_admin>
Écrit sur la sortie des lignes CLE=valeur (organisation, site, deux techniciens et leurs jetons).
Base locale uniquement : comptes et mots de passe de démonstration, sans donnée réelle.
"""
import json
import sys
import time
import urllib.request

MOT_DE_PASSE = "mot-de-passe-de-demonstration"


def appeler(api, chemin, corps, jeton=None):
    requete = urllib.request.Request(api + chemin, data=json.dumps(corps).encode(), method="POST")
    requete.add_header("Content-Type", "application/json")
    if jeton:
        requete.add_header("Authorization", "Bearer " + jeton)
    with urllib.request.urlopen(requete, timeout=30) as reponse:  # noqa: S310 (URL locale fournie par le script)
        contenu = reponse.read()
        return json.loads(contenu) if contenu else None


def connecter(api, email):
    return appeler(api, "/auth/login", {"email": email, "motDePasse": MOT_DE_PASSE})["jetonAcces"]


def main(api, jeton_activation, email_admin):
    if not api.startswith("http://localhost:"):
        sys.exit("API locale attendue (http://localhost:…).")
    appeler(api, "/auth/activate", {"jeton": jeton_activation, "motDePasse": MOT_DE_PASSE})
    admin = connecter(api, email_admin)
    moi = json.load(urllib.request.urlopen(urllib.request.Request(api + "/me", headers={"Authorization": "Bearer " + admin})))
    organisation = moi["organisations"][0]["id"]
    site = appeler(api, f"/organizations/{organisation}/sites", {"nom": "Site du POC"}, admin)["id"]
    sortie = {"PREP06_ORGANISATION": organisation, "PREP06_SITE": site}
    suffixe = str(int(time.time()))
    for nom in ("marc", "lea"):
        email = f"{nom}.{suffixe}@poc.test"
        cree = appeler(api, f"/organizations/{organisation}/members",
                       {"nom": nom, "email": email, "roles": [{"role": "Technicien", "siteId": site}]}, admin)
        appeler(api, "/auth/activate", {"jeton": cree["activation"]["jeton"], "motDePasse": MOT_DE_PASSE})
        sortie[f"PREP06_{nom.upper()}"] = cree["compteId"]
        sortie[f"PREP06_{nom.upper()}_JETON"] = connecter(api, email)
    for cle, valeur in sortie.items():
        print(f"{cle}={valeur}")


if __name__ == "__main__":
    main(*sys.argv[1:4])
