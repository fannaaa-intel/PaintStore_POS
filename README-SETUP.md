# PaintStore POS — install and update

---

# PART 1 — First time on a new laptop

Do these four once, in order.

### 1. Install XAMPP, start MySQL

Open the XAMPP Control Panel and press **Start** next to MySQL.

> Use XAMPP's MySQL. If MySQL Server 8.0 is also installed on the machine, do
> not use it for this project — running this project's SQL through the MySQL 8.0
> client against XAMPP's MariaDB corrupts tables.

### 2. Get the code

```
git clone https://github.com/fannaaa-intel/PaintStore_POS.git
```

### 3. Import the database

Open http://localhost/phpmyadmin → **Import** → choose
`paint_store_pos_full.sql` → **Go**.

The file creates the database itself, so there is nothing to make first.

### 4. Create the app's database login, and point the app at it

Two halves of the same step — the password has to match in both.

**4a.** Open `setup-database-user.sql`, replace `CHANGE_THIS_PASSWORD` (it
appears **twice**) with a password you choose. Run it in phpMyAdmin → **SQL**.

**4b.** Copy `App.config.example` to `App.config`, and copy
`PaintStore_POS.exe.config.example` to `PaintStore_POS.exe.config`. In both,
put that same password after `pwd=`:

```xml
connectionString="server=localhost;port=3306;database=paint_store_pos;uid=pos_app;pwd=YOUR_PASSWORD;"
```

### Done — open the solution in Visual Studio and press F5.

---

# PART 2 — Sending an update

## On your laptop

```
git add -A
git commit -m "what you changed"
git push origin main
```

## On their laptop

```
git pull
```

Then rebuild in Visual Studio (F5).

---

# PART 3 — The one thing to watch

`git pull` moves **code**. It never moves **data**.

| | Moves with git? |
|---|---|
| Your code: new screens, fixes, buttons | ✅ yes |
| Their data: sales, products, stock | ❌ no — stays on their machine |

Their sales staying put is what you want. But it means:

> **If your change added a new database column or table, `git pull` alone is not
> enough.** Their database still has the old shape and the app will error.

### When that happens

Tell them to also run **`migrations.sql`** in phpMyAdmin → SQL → Go.

That file is safe to run any number of times — every statement is written
`IF NOT EXISTS`, so anything already applied is skipped and their data is never
touched.

### How to know whether you need to tell them

| You changed | They run |
|---|---|
| Only VB code (screens, logic, colours) | `git pull` |
| Anything in `migrations.sql` | `git pull` **+ `migrations.sql`** |

When in doubt, have them run `migrations.sql` anyway. It is harmless.

---

# ⚠️ Never send them paint_store_pos_full.sql

That file begins with `DROP DATABASE` — importing it **erases everything** and
replaces it with your test data. Their real sales would be gone.

| File | When |
|---|---|
| `paint_store_pos_full.sql` | **First install only**, on an empty machine |
| `migrations.sql` | Every update that changes the database shape |

---

# If it will not start

**"Could not reach the database"**
MySQL is not running in XAMPP, or `App.config` is missing.

**"Access denied for user 'pos_app'"**
Step 4a was skipped, or the password in `App.config` does not match the one in
`setup-database-user.sql`.

**"Unknown database 'paint_store_pos'"**
Step 3 did not run.

**"Unknown column ..." after an update**
They pulled the code but not the schema — run `migrations.sql`.
