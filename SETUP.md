# PaintStore POS — Setup Guide

Three things you will ever need to do:

| | |
|---|---|
| [**A. First time on a new laptop**](#a-first-time-on-a-new-laptop) | Once per machine |
| [**B. Working on your own laptop**](#b-working-on-your-own-laptop) | Every time you finish a change |
| [**C. Updating someone else's laptop**](#c-updating-someone-elses-laptop) | When they want your changes |

---

# A. First time on a new laptop

Six steps, once.

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

## 4. Create the app's MySQL login

phpMyAdmin → **SQL** tab → paste this → **Go**:

```sql
CREATE USER 'pos_app'@'localhost' IDENTIFIED BY 'mypassword123';
GRANT SELECT, INSERT, UPDATE, DELETE ON paint_store_pos.* TO 'pos_app'@'localhost';
FLUSH PRIVILEGES;
```

Change `mypassword123` to anything you like — just remember it for step 5.

<details>
<summary>Why this step exists</summary>

The app does not connect to MySQL as `root`. It uses a limited account,
`pos_app`, that can read and write rows but cannot `DROP` or `ALTER` anything —
so a bug or an injected statement cannot destroy the schema.

That account lives in **MySQL's own user list**, not inside the
`paint_store_pos` database. Importing the database therefore does not bring it
along, and it has to be created once per machine.

</details>

## 5. Tell the app that password

In the project folder:

- Rename `App.config.example` → **`App.config`**
- Rename `PaintStore_POS.exe.config.example` → **`PaintStore_POS.exe.config`**

Open **both** in Notepad and find the line containing `pwd=`. Make it read:

```
uid=pos_app;pwd=mypassword123;
```

> ⚠️ The password must be **identical** in step 4 and step 5. That is the whole
> trick — step 4 creates the login, step 5 tells the app what it is.

<details>
<summary>Why these files are not in the repo</summary>

They hold a real password. Anything committed to git stays in the history even
after it is edited out, so these two are gitignored and the `.example` copies
are committed in their place.

</details>

## 6. Run it

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
| `App.config` | ❌ no, deliberately — it holds a password |

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
MySQL is not started in the XAMPP Control Panel, or `App.config` is missing.

**"Access denied for user 'pos_app'"**
Step 4 was skipped, or the password in `App.config` does not match the one in
the `CREATE USER` statement. Re-run step 4 with the password that is actually in
`App.config`.

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
| `setup-database-user.sql` | The step 4 `CREATE USER`, as a file you can paste. |
| `App.config.example` | Template for the connection file you create in step 5. |
| `Tests\run-tests.cmd` | Runs the 47 sale-math tests (VAT, discounts, change). |
