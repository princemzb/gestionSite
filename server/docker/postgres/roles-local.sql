-- Rôles PostgreSQL locaux (développement et tests), valeurs non sensibles. Idempotent.
-- Exécuté par le conteneur local à la création de la base (docker-entrypoint-initdb.d) et par les
-- tests d'intégration sur chaque base de test. La préproduction crée ses rôles sans ce fichier.
--   gestionsite_migrator : propriétaire des schémas, applique les migrations ; soumis à la RLS (FORCE).
--   gestionsite_app      : rôle de l'API ; aucun droit de modifier le schéma, soumis à la RLS.
-- Aucun des deux n'est superutilisateur ni BYPASSRLS (un superutilisateur ignore toujours la RLS).
DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'gestionsite_migrator') THEN
    CREATE ROLE gestionsite_migrator LOGIN PASSWORD 'local-dev-migrator-password' NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB;
  END IF;
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'gestionsite_app') THEN
    CREATE ROLE gestionsite_app LOGIN PASSWORD 'local-dev-app-password' NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB;
  END IF;
  EXECUTE format('GRANT CONNECT, CREATE ON DATABASE %I TO gestionsite_migrator', current_database());
  EXECUTE format('GRANT CONNECT ON DATABASE %I TO gestionsite_app', current_database());
END
$$;
