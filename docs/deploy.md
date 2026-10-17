# Deploying Spellbound

One small Linux server runs two containers from `docker-compose.yml`:

- **app**: the Spellbound web app with its embedded RavenDB. Its data lives on the `spellbound-data` volume
  (`/data` in the container): the database and the keys that protect sign-in cookies.
- **caddy**: the only thing exposed to the internet. It gets and renews a Let's Encrypt certificate for your domain,
  redirects HTTP to HTTPS and forwards everything, websockets included, to the app.

## What you need

- A VPS with **2 vCPU and 4 GB RAM** or more, running Ubuntu 24.04 (for example a Hetzner CX22 in the EU).
  The image is about 1.3 GB, mostly the RavenDB server. Keep the core count within your RavenDB licence's limit.
- A **domain** (or subdomain) for the game.
- A **RavenDB Community licence key** (free, from ravendb.net). A Developer key is not allowed in production.
  See [Architecture](architecture.md#persistence-notes).

## First deployment

1. **Point the domain at the server**: an `A` record (and `AAAA` for IPv6) to the server's address. Wait until
   `dig +short your.domain` shows it; Caddy needs it to get a certificate.
2. **Install Docker** with the compose plugin, following Docker's guide for Ubuntu
   (https://docs.docker.com/engine/install/ubuntu/).
3. **Open only SSH, HTTP and HTTPS** in the firewall: ports 22, 80 and 443 (TCP), and 443/UDP for HTTP/3.
4. **Get the code and settings**:

   ```sh
   git clone https://github.com/bisforboman/Spellbound.git
   cd Spellbound
   cp .env.example .env
   nano .env   # DOMAIN, ADMIN_EMAIL, and RAVEN_LICENSE (the licence JSON on one line, in single quotes)
   ```

   `.env` holds the licence key. It is gitignored; never commit it.
5. **Start**:

   ```sh
   docker compose up -d --build
   docker compose logs -f app   # wait for "Round 1 started", then Ctrl+C
   ```

   If the licence is missing or invalid, the app stops at once and the log says why.
6. **Open `https://your.domain`**, register with the `ADMIN_EMAIL` address, and check that the top bar shows **Admin**.

## Updating

```sh
cd Spellbound
git pull
docker compose up -d --build
```

The database and sign-ins survive: they live on the volume, not in the image. Players see a short reconnect while
the app restarts.

## Day to day

- **Logs**: `docker compose logs -f app` (or `caddy`).
- **Health**: `https://your.domain/healthz` answers `Healthy` while the app is up.
- **Change a setting** (`GameRules__...`, admins): add it to the `environment` list of `app` in
  `docker-compose.yml` or to `.env`, then `docker compose up -d`.
- **Licence renewal**: the Community key lasts a year. Put the new JSON in `.env` and run `docker compose up -d`.
  RavenDB also requires the latest major version, so update the `RavenDB.*` packages when a new major version ships.

## Backups

Proper nightly backups are the next roadmap item. Until then, a manual copy of the volume, with the app stopped:

```sh
docker compose stop app
docker run --rm -v spellbound_spellbound-data:/data -v "$PWD":/backup alpine \
    tar czf /backup/spellbound-$(date +%F).tar.gz -C /data .
docker compose start app
```

The volume name starts with the folder name (`spellbound_` for a clone in `Spellbound/`); `docker volume ls` shows it.
