# US Regional Admin Metadata Design

## Goal

Make the US Regional M1 v3.3 admin metadata required by `us-regional.xml` maintainable through the existing sequence publishing metadata API and available to the eCTD package model.

This closes the first practical gap after wiring real package generation: the US regional writer intentionally fails fast when applicant contact, telephone, or email metadata is missing, but the application currently has no storage/API path for those fields.

## Scope

In scope:

- Extend `SequencePublishingMetadata` with:
  - `ApplicantContactName`
  - `ApplicantContactType`
  - `Telephone`
  - `TelephoneNumberType`
  - `Email`
- Extend application request and DTO records with the same optional fields.
- Extend `UpdateSequencePublishingMetadataRequestBody` and controller mapping.
- Extend EF Core sequence persistence and migrations.
- Extend `EctdPackageModelBuilder` to populate `EctdUsRegionalMetadata` from these fields.
- Add backend tests for service round-trip, API round-trip, EF persistence, and package model mapping.

Out of scope:

- Frontend form changes.
- Application-level metadata defaults.
- Contact list/multiple contacts.
- Full FDA controlled terminology validation for contact/phone type values.
- DTD validation.

## Design

Use the existing sequence-level metadata pipe:

`PUT /api/applications/{id}/sequences/{sequenceNumber}/publishing-metadata`

The new fields are optional at the API and domain level. Empty or whitespace input normalizes to `null`, matching current optional metadata behavior. During package model construction, nulls map to empty strings so `UsRegionalXmlWriter` continues to enforce required-field failure at publish time.

This keeps the update endpoint usable for partial regulatory readiness workflows while preserving fail-fast behavior when a user attempts a real package without required FDA M1 admin data.

## Persistence

Add nullable columns to `sequences`:

- `FdaApplicantContactName` length 256
- `FdaApplicantContactType` length 64
- `FdaTelephone` length 64
- `FdaTelephoneNumberType` length 64
- `FdaEmail` length 256

Existing records remain valid. Existing metadata rows with core FDA fields but no admin contact fields should still rehydrate; the missing admin fields become null and the writer fails only when package generation requires them.

## Testing

Add tests that prove:

- The application service returns null defaults for the new fields and persists updated values.
- The HTTP API round-trips the new fields.
- EF repository saves and reloads the new fields.
- `EctdPackageModelBuilder` maps the fields to `EctdUsRegionalMetadata`.
- Full backend tests pass.
