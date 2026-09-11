# Setting up on another computer

Everything the app needs is in this repo except one file: `App.config`, which
holds the database password and is deliberately **not** committed (a password in
git stays in the history forever, even after it is edited out).

So a fresh clone needs three things done by hand: install XAMPP, import the
database, and create `App.config`.

---

## 1. Install XAMPP

Download XAMPP and start **MySQL** from the XAMPP Control Panel.

The app expects MariaDB on `localhost:3306`, which is XAMPP's default.

> **Use XAMPP's MySQL, not MySQL Server 8.0.** If both are installed they fight
> over port 3306, and running this project's SQL through the MySQL 8.0 client
> against a MariaDB server corrupts tables. See the warning at the top of
> `migrations.sql`.

---

## 2. Import the database

`paint_store_pos_full.sql` in this repo is a complete export — schema, data and
all migrations already applied.

**In phpMyAdmin** (http://localhost/phpmyadmin):

1. Click **Import** at the top
2. Choose `paint_store_pos_full.sql`
3. Press **Go**

The file creates the `paint_store_pos` database itself, so there is no need to
make it first.

**Or on the command line:**

```
C:\xampp\mysql\bin\mysql.exe -u root < paint_store_pos_full.sql
```

> The file starts with `DROP DATABASE IF EXISTS paint_store_pos`. On a machine
> that already has data in that database, this **replaces** it.

---

## 3. Create the database account

The app does not connect as root. It uses a limited account that can read and
write rows but cannot drop or alter tables, so a bug or an injected statement
cannot destroy the schema.

Open `setup-database-user.sql` from this repo, replace `CHANGE_THIS_PASSWORD`
(it appears twice) with a password of your choosing, then run it in
phpMyAdmin's **SQL** tab.

To keep the same password as the original machine, open that machine's
`App.config` and copy the value after `pwd=`.

---

## 4. Create App.config

Copy `App.config.example` to `App.config`, then put the same password in it:

```xml
<add name="PosDb"
     connectionString="server=localhost;port=3306;database=paint_store_pos;uid=pos_app;pwd=put-a-strong-password-here;"
     providerName="MySql.Data.MySqlClient" />
```

Do the same for `PaintStore_POS.exe.config` (copy from
`PaintStore_POS.exe.config.example`). Both files are gitignored, so they stay on
that machine only.

---

## 5. Build and run

Open `PaintStore_POS.sln` in Visual Studio 2022 and press **F5**.

Sign in with the same accounts as on the original machine — the users came
across in the import.

---

## Keeping two machines in sync

**Code** syncs through git as normal:

```
git pull            # get the other machine's changes
git push            # send yours
```

**The database does not.** Git carries no data. To move a database between
machines, export it on the source and import it on the target:

```
C:\xampp\mysql\bin\mysqldump.exe -u root --databases paint_store_pos > paint_store_pos_full.sql
```

Two people selling on two machines will drift apart immediately — each has its
own sales, stock counts and shifts. For genuine shared use, point both machines
at one MySQL server by putting that server's address in `App.config` instead of
`localhost`, and grant the `pos_app` account from the other machine's host.

---

## If something goes wrong

**"Could not reach the database"** — MySQL is not running in the XAMPP Control
Panel, or `App.config` is missing or has the wrong password.

**"Access denied for user 'pos_app'"** — the account was not created (step 3),
or its password does not match `App.config`.

**"Unknown database 'paint_store_pos'"** — the import in step 2 did not run.

**"#1932 doesn't exist in engine"** — a table's metadata was written by the wrong
client. Recovering needs a DISCARD/IMPORT TABLESPACE rebuild; the header of
`migrations.sql` explains the cause.
