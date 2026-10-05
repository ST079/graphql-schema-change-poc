# 🚨 GraphQL Schema Change Report

**Service:** GraphQL API

**Generated:** 2026-10-05 17:56 UTC

**Compared:** `./demo/old-schema.graphql` → `./demo/new-schema.graphql`

## Summary

| Metric | Count |
|---|---:|
| Total Changes | 3 |
| 🔴 Breaking | 1 |
| 🟡 Warning | 0 |
| 🟢 Informational | 2 |
| Affected Clients | 2 |
| Affected Operations | 3 |

## 🔴 Breaking Changes

### `Campaign.videoUrl`

**Change:** Field removed

**Previous type:** `String`

**Severity:** 🔴 Breaking

**Reason:**  
An existing field was removed and clients using it may fail.

#### Affected Clients

**Android**
- `GetCampaign`
- `GetCampaignDetails`

**Frontend**
- `CampaignDetails`

#### Recommended Action

Update affected client operations to stop requesting `Campaign.videoUrl`.

> **Possible migration candidate:**  
> `Campaign.videoUrl` → `Campaign.video`  
>
> Developer verification required.

## 🟢 Informational Changes

### `Campaign.video`

**Change:** Field added

**New type:** `CampaignVideo`

**Severity:** 🟢 Info

**Reason:**  
A new field was added. Existing clients can continue using the previous schema.

#### Recommended Action

No action required for existing clients unless they want to use the new field.

### `CampaignVideo`

**Change:** Type added

**Severity:** 🟢 Info

**Reason:**  
A new GraphQL type was added. Existing clients are not required to use it.

#### Recommended Action

No action required for existing clients unless they need the new GraphQL type.

## Client Impact

### 📱 Android

Affected operations:

- `GetCampaign` — `Campaign.videoUrl` (🔴 Breaking)
- `GetCampaignDetails` — `Campaign.videoUrl` (🔴 Breaking)

### 🌐 Frontend

Affected operations:

- `CampaignDetails` — `Campaign.videoUrl` (🔴 Breaking)

## ✅ Unaffected Operations

**Frontend**
- `CampaignCard`
- `GetUser`

## Changes

| Severity | Change | Schema Element | Previous | New |
|---|---|---|---|---|
| 🔴 Breaking | Field removed | `Campaign.videoUrl` | `String` | — |
| 🟢 Info | Field added | `Campaign.video` | — | `CampaignVideo` |
| 🟢 Info | Type added | `CampaignVideo` | — | — |

## Recommendation

The affected Android and Frontend operations should be updated before the new schema is consumed by those clients.
