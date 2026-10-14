# 06 — Daily operations

For the manager or the person looking after the system. Five minutes a day, ten a week.

## Every day

| When | Check |
|---|---|
| Opening | All terminals show 🟢 Connected. Kitchen screens show their station. Printers print a test KOT. |
| Opening | **Dashboard**: yesterday closed cleanly (no pending bills left from yesterday). |
| During service | A red banner or toast with a correlation id: note the id and time for support. |
| Closing | No open bills left on the billing queue; tables released. |
| Closing | Sign out of shared terminals. Leave the server on (it takes the backup at night). |

## Every week

- **Backup** screen: the last 7 days show OK, no red banner. Copy the newest backup to the off-site USB drive
  ([03](03-backup-and-recovery.md)).
- **Audit Log**: filter today/7 days for `Bill.Voided`, `Payment.Refunded`, `Bill.DiscountApplied` and
  `Auth.LoginFailed`; anything unexpected is worth a conversation.
- `/health` on the server says Healthy (Degraded means disk space is running out).

## People

- One account per person; never share passwords. When someone leaves: **Users → Deactivate** — do not delete, the
  audit trail keeps their name. They cannot sign in again and their sessions are not renewed, but a session already
  open can last up to an hour; if that matters (e.g. a dismissal), also deactivate the terminal they are using.
- A forgotten password: **Users → Reset password** (they must change it at sign-in). A locked account unlocks by
  itself after 15 minutes or with **Unlock**.
- New terminal: install the client ([01](01-installation.md) § 5); it registers itself at the first sign-in.
  A lost or stolen tablet: **Devices → Deactivate** at once.

## Before maintenance or a restart

**Administration → Server → Send notice** a few minutes ahead, so staff finish what they are doing.
