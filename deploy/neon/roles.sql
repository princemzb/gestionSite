-- Rôles de préproduction (DEP-01), à exécuter une fois avec le rôle propriétaire de la base Neon :
--   psql "<chaîne du propriétaire>" -v mdp_migrator='…' -v mdp_app='…' -f deploy/neon/roles.sql
-- Les mots de passe viennent de la ligne de commande (générés, gardés dans le Trousseau), jamais du dépôt.
-- Relançable : crée les rôles absents et remet leurs mots de passe (rotation).
--   gestionsite_migrator : propriétaire des schémas, applique les migrations ; soumis à la RLS (FORCE).
--   gestionsite_app      : rôle de l'API ; aucun droit de modifier le schéma, soumis à la RLS.
\set ON_ERROR_STOP on

SELECT format('CREATE ROLE gestionsite_migrator LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB', :'mdp_migrator')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'gestionsite_migrator') \gexec
SELECT format('ALTER ROLE gestionsite_migrator WITH LOGIN PASSWORD %L', :'mdp_migrator') \gexec

SELECT format('CREATE ROLE gestionsite_app LOGIN PASSWORD %L NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB', :'mdp_app')
WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'gestionsite_app') \gexec
SELECT format('ALTER ROLE gestionsite_app WITH LOGIN PASSWORD %L', :'mdp_app') \gexec

SELECT format('GRANT CONNECT, CREATE ON DATABASE %I TO gestionsite_migrator', current_database()) \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO gestionsite_app', current_database()) \gexec

SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolname LIKE 'gestionsite_%' ORDER BY rolname;
