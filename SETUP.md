# PaintStore POS — Setup Guide

Three things you will ever need to do:

| | |
|---|---|
| [**A. First time on a new laptop**](#a-first-time-on-a-new-laptop) | Once per machine |
| [**B. Working on your own laptop**](#b-working-on-your-own-laptop) | Every time you finish a change |
| [**C. Updating someone else's laptop**](#c-updating-someone-elses-laptop) | When they want your changes |

---

# A. First time on a new laptop

Four steps, once.

## 1. Install XAMPP and start MySQL

Install XAMPP, open the **XAMPP Control Panel**, press **Start** next to MySQL.

> **Use XAMPP's MySQL.** If MySQL Server 8.0 is also on the machine, do not use
> it for this project. Running this project's SQL through the MySQL 8.0 client
> against XAMPP's MariaDB corrupts tables and needs a manual rebuild to recover.

## 2. Get the code

Open a terminal in a short folder such as `C:\Users\<name>\source\repos` and run:

```
git clone -c core.longpaths=true https://github.com/fannaaa-intel/PaintStore_POS.git
```

> Some files inside `packages\` have long names. Without `core.longpaths=true`,
> cloning into a deeply nested folder fails with **"Filename too long"**. Keep
> the folder you clone into short (under about 120 characters) either way.

## 3. Import the database

Two imports, in this order:

1. Open <http://localhost/phpmyadmin> → **Import** tab → **Choose File** →
   `paint_store_pos_full.sql` → **Go**. The file creates the database itself,
   so there is nothing to create first.
2. Click the new **paint_store_pos** database on the left → **SQL** tab →
   paste the whole contents of **`migrations.sql`** → **Go**.

Step 2 is not optional. `paint_store_pos_full.sql` is an older export and is
missing the newer columns and tables (cheques, recycle bin, Senior/PWD and
more). `migrations.sql` adds them. Without it the log-in screen shows
**"Database Update Needed"**.

## 4. Email sender (mail.config)

Password-change codes and "account created" notices are sent from the store's
Gmail. The sender lives in `mail.config` next to `PaintStore_POS.exe`. It holds
a Google App Password, so **git does not carry it**:

1. Copy `mail.config.example` to `mail.config`.
2. Fill in the Gmail address and its **App Password**
   (myaccount.google.com/apppasswords, needs 2-Step Verification on). This is not
   the normal Gmail password.

Without it the app still runs, but password changes that need a code will
report that email is not set up.

## 5. Run it

Open `PaintStore_POS.sln` in Visual Studio 2022 → press **F5**.

Sign in with the same app accounts as the original machine — those came across
in the import.

---

# B. Working on your own laptop

After finishing a change:

```
git add -A
git commit -m "what you changed"
git push origin main
```

---

# C. Updating someone else's laptop

```
git pull
```

Then open `PaintStore_POS.sln` in Visual Studio → **Build → Rebuild Solution**
(or **F5**). Git does not carry `PaintStore_POS.exe`; every laptop builds its
own, so a pull never conflicts with the exe that laptop built last time.

> **Laptops last updated before Oct 21 2026** still have the old tracked exe.
> The first pull after that date needs one extra line first:
>
> ```
> git checkout -- PaintStore_POS.exe PaintStore_POS.pdb PaintStore_POS.xml
> git pull
> ```
>
> The pull then removes the old exe — rebuild in Visual Studio to make the new one.

> **Laptops last updated before Oct 30 2026:** the first pull after that date
> removes `PaintStore_POS.exe.config` (git no longer carries it). The program
> will not start until you **Rebuild** in Visual Studio, which makes a fresh one
> from `App.config`.

## If your change touched the database

`git pull` brings **code**. It does not change the shape of their database. So
if you added a column or a table, they must also run:

> phpMyAdmin → **SQL** tab → paste the contents of **`migrations.sql`** → **Go**

Safe to run any number of times: every statement is written `IF NOT EXISTS`, so
anything already applied is skipped and existing data is never touched.

| You changed | They run |
|---|---|
| Only VB code — screens, logic, colours | `git pull` |
| Anything in `migrations.sql` | `git pull` **+ `migrations.sql`** |

When in doubt, have them run `migrations.sql`. It is harmless.

---

# What git does and does not carry

| | Travels with git? |
|---|---|
| Your code — new screens, fixes, buttons | ✅ yes |
| `PaintStore_POS.exe` and `.exe.config` — the built program | ❌ no, rebuild in Visual Studio after each pull |
| Their data — sales, products, stock counts | ❌ no, stays on their machine |
| Database shape — new columns and tables | ❌ no, that is what `migrations.sql` is for |
| `mail.config` — the Gmail App Password | ❌ no, copy it by hand (see A.4) |
| `Backups\` — database backups | ❌ no, copy them to a USB drive yourself |

Their sales staying on their machine is the behaviour you want. Two laptops
selling at once are two separate shops; for one shared set of live data, point
both `App.config` files at one MySQL server instead of `localhost`.

---

# 🚨 Never import paint_store_pos_full.sql onto a working machine

That file begins with `DROP DATABASE`. Importing it **erases everything** and
replaces it with the exported data — real sales included.

| File | When to use it |
|---|---|
| `paint_store_pos_full.sql` | **First install only**, on an empty machine |
| `migrations.sql` | Every update that changes the database shape |

---

# Troubleshooting

**`git pull` says "Your local changes to the following files would be overwritten"**
Someone edited a file on that laptop, usually `App.config` (e.g. a MySQL
password). Keep the edit and still get the update:

```
git stash
git pull
git stash pop
```

If `git stash pop` reports a conflict in `App.config`, open it, keep both the
new lines from the update and the laptop's own password/server, then save.

**"Could not reach the database"**
MySQL is not started in the XAMPP Control Panel.

**"Unknown database 'paint_store_pos'"**
The import in step 3 did not run.

**"Database Update Needed" at the log-in screen, or "Unknown column ..." right after an update**
The code was pulled but the schema was not — run `migrations.sql`. The log-in
warning names the sections that are missing.

**XAMPP's MySQL will not start (port 3306 in use)**
MySQL Server 8.0 is installed as a Windows service and took the port. Open
`services.msc`, stop **MySQL80** and set its Startup type to **Manual**.

**"Unexpected Error" message**
The app caught an error and kept running. The details are in
`%LOCALAPPDATA%\PaintStore_POS\Logs\errors-<year>-<month>.log` — send that file
to the developer.

**"#1932 — Table doesn't exist in engine"**
A table's metadata was written by the wrong MySQL client. The data file is
usually intact; recovery needs a `DISCARD` / `IMPORT TABLESPACE` rebuild. The
header of `migrations.sql` explains the cause and how to avoid it.

---

# Files in this repo

| File | What it is |
|---|---|
| `paint_store_pos_full.sql` | Complete database — schema and data. First install only. |
| `migrations.sql` | Schema changes only. Safe to re-run; use for updates. |
| `Database\paint_store_pos.sql` | phpMyAdmin export (Sep 28 2026, schema up to migration 22). Import into an **empty** `paint_store_pos` database, then run `migrations.sql` to bring it up to date. |
| `Tests\run-tests.cmd` | Runs the 47 sale-math tests (VAT, discounts, change). |
