# Torn API Key Terms Disclosure

Source policy: https://www.torn.com/api.html#

When a user submits an API key to this service:

| Data Storage | Data Sharing | Purpose of Use | Key Storage & Sharing | Key Access Level |
|---|---|---|---|---|
| Persistent - forever (imported/derived data retained in SQLite until manually removed) | General public | Non-malicious statistical analysis | API key is not stored and not shared. It is sent over HTTPS for import execution and kept in memory only during the run. | Full Access |

Additional commitments:
- We only request the API key itself (never Torn password).
- Key usage is limited to the import/reconstruction features described in project docs.
- Users should not submit a key if they do not accept retained/public data behavior.
