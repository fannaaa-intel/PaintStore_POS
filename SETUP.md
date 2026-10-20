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

```
git clone https://github.com/fannaaa-intel/PaintStore_POS.git
```

## 3. Import the database

Open <http://localhost/phpmyadmin> → **Import** tab → **Choose File** →
`paint_store_pos_full.sql` → **Go**.

The file creates the database itself, so there is nothing to create first.

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

Then rebuild in Visual Studio (**F5**).

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

**"Could not reach the database"**
MySQL is not started in the XAMPP Control Panel.

**"Unknown database 'paint_store_pos'"**
The import in step 3 did not run.

**"Unknown column ..." right after an update**
The code was pulled but the schema was not — run `migrations.sql`.

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
