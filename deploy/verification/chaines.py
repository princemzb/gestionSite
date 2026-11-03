"""Chaîne de connexion (format Npgsql) d'un rôle de la base de préproduction, construite depuis le Trousseau macOS.

Usage : python3 -I chaines.py migrator|app — la chaîne est écrite sur la sortie standard pour être capturée dans une
variable par lancer.sh, jamais affichée ni écrite sur disque. Services lus : gestionsite-preprod-neon-owner (hôte et base),
gestionsite-preprod-pg-migrator et gestionsite-preprod-pg-app (mots de passe des rôles, DEPLOIEMENT étapes 1 et 2).
"""
import subprocess
import sys
from urllib.parse import urlparse

SERVICES = {"migrator": "gestionsite-preprod-pg-migrator", "app": "gestionsite-preprod-pg-app"}


def trousseau(service):
    return subprocess.run(["security", "find-generic-password", "-s", service, "-w"],
                          capture_output=True, text=True, check=True).stdout.strip()


def main():
    if len(sys.argv) != 2 or sys.argv[1] not in SERVICES:
        sys.exit("usage : chaines.py migrator|app")
    role = sys.argv[1]
    proprietaire = urlparse(trousseau("gestionsite-preprod-neon-owner"))
    print(f"Host={proprietaire.hostname};Database={proprietaire.path.lstrip('/')};Username=gestionsite_{role};"
          f"Password={trousseau(SERVICES[role])};SSL Mode=Require")


if __name__ == "__main__":
    main()
