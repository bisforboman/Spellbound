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
- **Forgotten passwords**: the server sends no email, so players ask you. On the admin page, enter their email under
  Players → **Reset a password by email** (or tap **Reset password…** on their row): you get a temporary password,
  shown once, to give them privately. They log in with it and choose a new one under Account. This also lifts a
  lockout from wrong guesses (not a ban).
- **Licence renewal**: the Community key lasts a year. Put the new JSON in `.env` and run `docker compose up -d`.
  RavenDB also requires the latest major version, so update the `RavenDB.*` packages when a new major version ships.

## Rate limits

The app limits requests per IP address (see `RateLimits` in [Architecture](architecture.md#persistence-notes)). A
mobile carrier or a school can put many players behind one address; if players report "Too many requests", raise the
limits with `RateLimits__AccountPosts`, `RateLimits__ConnectionRequestsPerMinute` or `RateLimits__RequestsPerMinute`.
If you put Cloudflare in front later, the app must read the player's address from Cloudflare's header instead of
Caddy's, or every player will share Cloudflare's addresses: do that in the same change.

## Backups

The app backs itself up: every night after 03:00 UTC it exports the whole database (game and accounts) to
`/data/backups/spellbound-<date>-<time>.ravendbdump` and keeps the newest 7. The admin page shows the last backup and
has a **Back up now** button. Settings, if you want to change them: `Backup__TimeOfDayUtc` (default `03:00:00`) and
`Backup__Keep` (default `7`).

These files sit on the same disk as the database, so also **copy them off the server**. For example, a nightly cron
job on the host that sends the backups folder to a Hetzner Storage Box (or any SSH server):

```sh
# crontab -e on the server; the volume's path is shown by: docker volume inspect spellbound_spellbound-data
30 4 * * * rsync -a /var/lib/docker/volumes/spellbound_spellbound-data/_data/backups/ u12345@u12345.your-storagebox.de:spellbound/
```

The volume name starts with the folder name (`spellbound_` for a clone in `Spellbound/`); `docker volume ls` shows it.

### Restoring

A restore goes into an **empty** database, so it can never overwrite a running game by accident:

1. `docker compose down` and move the old volume aside: `docker volume rm spellbound_spellbound-data` (after copying
   anything you want to keep).
2. Put the backup on a fresh volume **without starting the app** (a started app creates round 1, and then the
   database isn't empty any more). From the folder that holds the backup file:

   ```sh
   docker run --rm -v spellbound_spellbound-data:/data -v "$PWD":/in alpine sh -c \
       'mkdir -p /data/backups && cp /in/spellbound-<date>-<time>.ravendbdump /data/backups/ && chown -R 1654:1654 /data'
   ```

   (1654 is the app user in the image.)
3. Add `Backup__RestoreFrom: /data/backups/spellbound-<date>-<time>.ravendbdump` to the app's `environment` in
   `docker-compose.yml`, and run `docker compose up -d`. The log says "Restored the database from …".
4. Remove the `Backup__RestoreFrom` line again and run `docker compose up -d`. (If you forget, nothing happens: the
   app skips the restore once the database has data, and logs a warning.)
