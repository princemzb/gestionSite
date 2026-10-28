# Parcours complet sur la préproduction (organisation fictive de vérification) : activation de
# l'administrateur, écran membres, invitation en 3 étapes, activation du membre, connexion.
# Usage : python3 -I parcours_preprod.py <url_publique> <jeton_admin> <email_admin> ; mot de passe via MDP.
import html, http.cookiejar, os, re, sys, urllib.error, urllib.parse, urllib.request

BASE, JETON, EMAIL = sys.argv[1].rstrip("/"), sys.argv[2], sys.argv[3]
MDP = os.environ["MDP"]


class SansRedirection(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def navigateur():
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()), SansRedirection())


def appeler(nav, chemin, donnees=None):
    if chemin.startswith(BASE):
        chemin = chemin[len(BASE):]  # redirections en adresse absolue (https, hôte public)
    req = urllib.request.Request(BASE + chemin, data=urllib.parse.urlencode(donnees).encode() if donnees else None,
                                 headers={"User-Agent": "Mozilla/5.0 (verification gestionSite)"})
    try:
        with nav.open(req, timeout=90) as r:
            return r.status, html.unescape(r.read().decode()), r.headers
    except urllib.error.HTTPError as e:
        return e.code, html.unescape(e.read().decode()), e.headers


def poster(nav, page, formulaire, champs, action=None):
    statut, corps, _ = appeler(nav, page)
    assert statut == 200, (page, statut)
    jeton = re.search(r'name="__RequestVerificationToken" (?:type="hidden" )?value="([^"]+)"', corps).group(1)
    return appeler(nav, action or page, {"__RequestVerificationToken": jeton, "_handler": formulaire, **champs})


def verifier(nom, condition):
    print(("OK   " if condition else "ÉCHEC") + " " + nom)
    if not condition:
        sys.exit(1)


admin = navigateur()
statut, _, entetes = poster(admin, "/activation?jeton=" + urllib.parse.quote(JETON), "activation",
                            {"Saisie.Jeton": JETON, "Saisie.MotDePasse": MDP, "Saisie.Confirmation": MDP})
verifier("activation de l'administrateur (302)", statut == 302)
statut, _, entetes = appeler(admin, "/")
membres = entetes.get("Location", "")
statut, page, _ = appeler(admin, membres)
verifier("arrivée sur l'écran Membres", statut == 200 and "Qui fait quoi, par site" in page)
verifier("feuille de style référencée et servie", "app.css" in page and appeler(admin, "/app.css")[0] == 200)

statut, etape2, _ = poster(admin, membres + "?inviter=1", "invitation",
                           {"Saisie.Etape": "1", "Saisie.Nom": "Membre Vérification", "Saisie.Email": "membre@verif.gestionsite.test"})
site = re.search(r'name="Saisie.Roles\[0\].SiteId" value="([0-9a-f-]{36})"', etape2).group(1)
statut, etape3, _ = poster(admin, membres, "invitation",
                           {"Saisie.Etape": "2", "Saisie.Nom": "Membre Vérification", "Saisie.Email": "membre@verif.gestionsite.test",
                            "Saisie.Roles[0].SiteId": site, "Saisie.Roles[0].Role": "Technicien"})
lien = re.search(r'<code class="lien-activation">([^<]+)</code>', etape3).group(1)
verifier("invitation créée, lien d'activation affiché", "Invitation créée pour Membre Vérification" in etape3)
verifier("lien d'activation sur l'adresse publique en https", lien.startswith(BASE + "/activation?jeton="))

membre = navigateur()
jeton_membre = urllib.parse.unquote(lien.split("jeton=", 1)[1])
statut, _, _ = poster(membre, lien[len(BASE):], "activation",
                      {"Saisie.Jeton": jeton_membre, "Saisie.MotDePasse": MDP, "Saisie.Confirmation": MDP})
verifier("activation du membre (302)", statut == 302)
verifier("le membre (technicien) voit son espace", "Votre compte est actif" in appeler(membre, "/")[1])
verifier("le membre apparaît « Actif » chez l'administrateur", "Actif" in appeler(admin, membres)[1])

nouveau = navigateur()
statut, _, entetes = poster(nouveau, "/connexion", "connexion", {"Saisie.Email": EMAIL, "Saisie.MotDePasse": MDP})
verifier("connexion de l'administrateur depuis un autre navigateur (302)", statut == 302)
cookie = entetes.get("Set-Cookie", "")
verifier("cookie de session Secure et HttpOnly", "secure" in cookie.lower() and "httponly" in cookie.lower())
