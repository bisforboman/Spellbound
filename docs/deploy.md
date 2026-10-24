# Deploying Spellbound

One small Linux server runs three containers from `docker-compose.yml`:

- **app**: the Spellbound web app. Its `spellbound-data` volume (`/data` in the container) holds the nightly backups
  and the keys that protect sign-in cookies.
- **db**: PostgreSQL 16, with its data on the `postgres-data` volume. Only the app can reach it.
- **caddy**: the only thing exposed to the internet. It gets and renews a Let's Encrypt certificate for your domain,
  redirects HTTP to HTTPS and forwards everything, websockets included, to the app.

## What you need

- A VPS with **2 vCPU and 4 GB RAM** or more, running Ubuntu 24.04 (for example a Hetzner CX22 in the EU).
- A **domain** (or subdomain) for the game.
- A **Google OAuth client** for sign-in (free; see [Google sign-in](#google-sign-in) below).

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
   nano .env   # DOMAIN, ADMIN_EMAIL, POSTGRES_PASSWORD (letters and digits: openssl rand -hex 24),
               # GOOGLE_CLIENT_ID, GOOGLE_CLIENT_SECRET, and the PRIVACY_* lines (see Privacy notice)
   ```

   `.env` holds the database password and the Google client secret. It is gitignored; never commit it. Set the password before the first start:
   the database keeps the one it was created with.
5. **Start**:

   ```sh
   docker compose up -d --build
   docker compose logs -f app   # wait for "Application started", then Ctrl+C
   ```

   The very first start creates the tables; the log lists each "Applied database script".
6. **Open `https://your.domain`**, sign in with the Google account whose email is `ADMIN_EMAIL`, and check that the top
   bar shows **Admin**.

## Privacy notice

The game shows a privacy notice at `/privacy`, linked from the sign-in page, the top bar when signed out, and the Account page. It describes what
the app stores, written from the code. **Read it before launch**: you run the game, so you're responsible for its data
(this is a description of the software, not legal advice). It fills in from `.env`:

- `PRIVACY_OPERATOR`: who runs the game, e.g. your name.
- `PRIVACY_CONTACT`: how players reach you about their data, e.g. an email address. It's shown publicly.
- `PRIVACY_HOSTING`: where the server is, e.g. "a rented server in Germany (Hetzner)".
- `PRIVACY_OFFSITE_BACKUPS` (optional): where you keep copies of the backups and for how long, e.g. "on a storage box
  for 30 days". Leave it empty if you don't (but see Backups: you should).

Without the first three the app won't start. If you change what the server does with data (for example put Cloudflare
in front, which sees players' addresses, or keep off-site backups longer), update the notice to match: its text is in
`src/Spellbound.Web/Components/Account/Pages/Privacy.razor`.

## Google sign-in

Players sign in with Google; the server stores no passwords. To set it up (once, free):

1. In [Google Cloud Console](https://console.cloud.google.com/), create a project (any name, e.g. "Spellbound").
2. **Google Auth Platform** (older layouts: APIs & Services → OAuth consent screen):
   - **Branding:** the app name players will see (e.g. "Spellbound") and your support email.
   - **Audience:** **External**. While it's in "Testing", only the test users you add can sign in: add yourself.
   - **Data access:** add nothing. The defaults (openid, email, profile) are all the game needs, so there's no review.
3. **Clients → Create client** (older: Credentials → OAuth client ID), type **Web application**, any name (e.g.
   "Spellbound web"; only you see it). Under **Authorized redirect URIs** add `https://localhost:7068/signin-google`
   to try it locally. Leave JavaScript origins empty. Copy the client secret now: it's shown once.
4. Put the client id and secret in `.env` as `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET`, then `docker compose up -d`.

**When you have the domain**, go back and:
- add `https://your.domain/signin-google` to the client's redirect URIs;
- fill in Branding's home page (`https://your.domain`), privacy policy (`https://your.domain/privacy`) and authorized
  domain;
- **publish** the app (Audience → Publish), so anyone can sign in, not just your test users.

Without them the app refuses to start outside Development, and the log says why. Locally, `dotnet user-secrets set
"Authentication:Google:ClientId" …` (and `ClientSecret`) in `src/Spellbound.Web` adds the Google button next to the
Development sign-in.

## Alerts

Every error the app logs (an unhandled error, a failed or overdue backup, a failed game check) is listed on the
admin page under **Problems**, since the app last started. To also get a push, set `ALERT_WEBHOOK_URL` in `.env`:

- **ntfy** (a notification on your phone): install the ntfy app, subscribe to a topic with a long, unguessable name,
  and set `ALERT_WEBHOOK_URL=https://ntfy.sh/<that-topic>`. Anyone who knows the topic can read it, which is why the
  name should be unguessable.
- **Discord**: in a channel of yours, Edit channel → Integrations → Webhooks → New webhook → Copy webhook URL, and set
  it as `ALERT_WEBHOOK_URL`.

A push only says where the error happened and its type, never the message (which can hold player data); the
details are on the admin page. Each kind is pushed at most once an hour, with a count of the repeats.

## Notifications

Players can turn on notifications on the Account page, with a test button (the game sends no others yet). Nothing needs setting
up: the server makes its own key (VAPID) on first use and keeps it in the database, and the privacy notice's contact
(`PRIVACY_CONTACT`) is given to push services as the server's contact. It needs HTTPS, which Caddy provides. **Keep the
database's `server_secrets` table** (backups include it): a new key would silently stop every player's notifications
until they turn them on again.

On iPhone and iPad, notifications only work from the home-screen app (Safari's Share, then Add to Home Screen, iOS 16.4
or later); the Account page says so. Use **Send me a test notification** on your own phone once after deploying.

## Updating

```sh
cd Spellbound
git pull
docker compose up -d --build
```

The database and sign-ins survive: they live on volumes, not in the image. A new version that changes the tables
updates them when it starts. Players see a short reconnect while the app restarts.

## Day to day

- **Logs**: `docker compose logs -f app` (or `db`, `caddy`).
- **Look at the data**: `docker compose exec db psql -U spellbound` (for example `select id, mode, number, ends_at from games where ended_at is null;`, the games running).
- **Health**: `https://your.domain/healthz` answers `Healthy` while the app is up.
- **Change a setting** (admins, rate limits, …): add it to the `environment` list of `app` in
  `docker-compose.yml` or to `.env`, then `docker compose up -d`.
- **Sign-in problems** are Google's to solve (a forgotten Google password, a lost phone); the game has nothing to reset.
  A player who loses their Google account loses their game account with it.
- **PostgreSQL version**: the compose file pins `postgres:16`, the same major version as the backup tools in the app
  image. Minor updates come with `docker compose pull db && docker compose up -d`. A new major version needs a dump
  and restore (as in Restoring, below) and a newer client in the `Dockerfile`, so it's a planned change, not an update.

## Game rules

New matches start under the rules in `GameRules` (`src/Spellbound.Domain/GameRules.cs`, explained in
[game-design.md](game-design.md)). To try other numbers without a new build, set them in the environment, for example
`GameRules__StartingMana=12` or `GameRules__Rounds=6`; a list is given item by item (`GameRules__WildYields__0=0`,
`GameRules__WildYields__1=4`, …) and replaces the whole default list. Restart the app to apply them. Each match keeps the
rules it started with, so matches already running don't change.

## Rate limits

The app limits requests per IP address (see `RateLimits` in [Architecture](architecture.md#persistence-notes)). A
mobile carrier or a school can put many players behind one address; if players report "Too many requests", raise the
limits with `RateLimits__AccountPosts`, `RateLimits__ConnectionRequestsPerMinute` or `RateLimits__RequestsPerMinute`.
If you put Cloudflare in front later, the app must read the player's address from Cloudflare's header instead of
Caddy's, or every player will share Cloudflare's addresses: do that in the same change.

## Backups

The app backs itself up: every night after 03:00 UTC it dumps the whole database (game and accounts) with `pg_dump`
to `/data/backups/spellbound-<date>-<time>.dump` and keeps the newest 7. The admin page shows the last backup and
has a **Back up now** button. Settings, if you want to change them: `Backup__TimeOfDayUtc` (default `03:00:00`) and
`Backup__Keep` (default `7`).

These files sit on the same disk as the database, so also **copy them off the server**. For example, a nightly cron
job on the host that sends the backups folder to a Hetzner Storage Box (or any SSH server):

```sh
# crontab -e on the server; the volume's path is shown by: docker volume inspect spellbound_spellbound-data
30 4 * * * rsync -a --delete /var/lib/docker/volumes/spellbound_spellbound-data/_data/backups/ u12345@u12345.your-storagebox.de:spellbound/
```

The volume name starts with the folder name (`spellbound_` for a clone in `Spellbound/`); `docker volume ls` shows it.
With `--delete` the copy mirrors the server, so it holds the same last 7 backups and deleted accounts leave it within
a week. Without it, every backup is kept forever, and the privacy notice would have to say so: set
`PRIVACY_OFFSITE_BACKUPS` to whatever you choose.

### Restoring

A restore goes into an **empty** database (one the app has never started on), so it can never overwrite a running
game by accident:

1. `docker compose down`, then remove the database volume: `docker volume rm spellbound_postgres-data` (after making
   sure you have the backup you want).
2. Put the backup on the app's volume. From the folder that holds the backup file:

   ```sh
   docker run --rm -v spellbound_spellbound-data:/data -v "$PWD":/in alpine sh -c \
       'mkdir -p /data/backups && cp /in/spellbound-<date>-<time>.dump /data/backups/ && chown -R 1654:1654 /data'
   ```

   (1654 is the app user in the image.)
3. Add `Backup__RestoreFrom: /data/backups/spellbound-<date>-<time>.dump` to the app's `environment` in
   `docker-compose.yml`, and run `docker compose up -d`. The fresh database starts empty, the app restores into it
   before anything else, then updates the tables if the backup is from an older version. The log says "Restored the
   database from …".
4. Remove the `Backup__RestoreFrom` line again and run `docker compose up -d`. (If you forget, nothing happens: the
   app skips the restore once the database has data, and logs a warning.)
