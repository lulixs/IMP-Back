# Docker Guide – IMP Database

This folder runs our MySQL database in Docker, so everyone gets the same database version and tables.

- `docker-compose.yml` – tells Docker how to run MySQL
- `schema.sql` – creates the `IMP` database and all tables

---

## One-time setup

1. Install **Docker Desktop**: https://www.docker.com/products/docker-desktop/
2. If Docker complains about WSL or virtualization, open **PowerShell as Administrator** and run:
   ```
   wsl --install
   ```
   Then **restart your computer**.
3. Open **Docker Desktop** and leave it running. It must be open for any of the commands below to work.

---

## Opening PowerShell in the right folder

All commands must be run **from this folder** (the one with `docker-compose.yml` in it).

**Option A – from File Explorer (easiest)**
1. Open this folder in File Explorer.
2. Right-click an empty spot in the folder.
3. Click **Open in Terminal**.
   - On older Windows: hold **Shift**, right-click, and choose **Open PowerShell window here**.

**Option B – type the path**
1. Open PowerShell from the Start menu.
2. Use `cd` with the folder path **in quotes** (the folder name has spaces):
   ```
   cd "C:\path\to\back end repo"
   ```
   Tip: click the address bar in File Explorer, copy the path, and paste it between the quotes.

**Check you're in the right place:**
```
dir
```
You should see `docker-compose.yml` and `schema.sql`.

---

## Basic commands

| What you want | Command |
|---|---|
| Start the database (first time or after `down`) | `docker compose up -d` |
| Stop it (keeps your data) | `docker compose stop` |
| Start it again after `stop` | `docker compose start` |
| See if it's running | `docker compose ps` |
| See the logs / errors | `docker compose logs db` |
| **Reload after editing `schema.sql`** (wipes all data!) | `docker compose down -v` then `docker compose up -d` |
| Remove the container but keep data | `docker compose down` |

**List the tables to check everything loaded:**
```
docker exec -it imp-mysql mysql -uimp -pimp IMP -e "SHOW TABLES;"
```

**Open a MySQL prompt inside the container:**
```
docker exec -it imp-mysql mysql -uimp -pimp IMP
```
Type `exit` to leave.

---

## Connection details

Use these in the backend or a database tool (MySQL Workbench, DBeaver, etc.):

| Setting | Value |
|---|---|
| Host | `localhost` |
| Port | `3307` |
| Database | `IMP` |
| User | `imp` |
| Password | `imp` |
| Root password | `root` |

Port is **3307** (not the usual 3306) so it doesn't clash with a MySQL already installed on your computer.

---

## Common problems

- **`schema.sql` changes don't show up** – Docker only runs `schema.sql` when the database is brand new. Run `docker compose down -v` then `docker compose up -d`.
- **Some tables missing after a reset** – a line in `schema.sql` has an error. Run `docker compose logs db` and look for a line starting with `ERROR`.
- **"Cannot connect to the Docker daemon" / "error during connect"** – Docker Desktop isn't open. Start it and wait until it says it's running.
- **"no configuration file provided"** – you're in the wrong folder. See *Opening PowerShell in the right folder* above.
- **Port already in use** – something else is using 3307. Change the left number in `docker-compose.yml` (e.g. `"3308:3306"`), then `docker compose down` and `docker compose up -d`.
