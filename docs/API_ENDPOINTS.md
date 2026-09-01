# Dr. Care API v1 inventory

All routes require JWT authentication unless marked public. Tenant and role checks are enforced in the application layer.

## 1. Authentication and users

- `POST /api/v1/auth/login`
- `POST /api/v1/auth/refresh`
- `POST /api/v1/auth/logout`
- `POST /api/v1/auth/forgot-password`
- `POST /api/v1/auth/reset-password`
- `GET /api/v1/auth/me`
- `GET|POST /api/v1/users`
- `GET|PATCH /api/v1/users/{userId}`
- `POST /api/v1/users/{userId}/deactivate`

## 2. Reference and configuration

- `GET /api/v1/reference/pipeline-states`
- `GET /api/v1/reference/product-lines`
- `GET /api/v1/reference/document-types`
- `GET /api/v1/reference/task-types`
- `GET /api/v1/reference/checklists/{productLine}`
- `GET|PATCH /api/v1/settings/pricing`
- `GET|PATCH /api/v1/settings/invoice`
- `GET|POST /api/v1/settings/contract-templates`
- `GET|PATCH /api/v1/settings/pre-launch-checklists`
- `GET|PATCH /api/v1/settings/annual-goal`

## 3–7. Lead, inquiry, nurturing, activities, and tasks

- `GET|POST /api/v1/leads`
- `GET /api/v1/leads/{leadId}`
- `POST /api/v1/leads/{leadId}/inquiry/start`
- `GET|PATCH /api/v1/leads/{leadId}/inquiry`
- `POST /api/v1/leads/{leadId}/inquiry/submit`
- `GET|PATCH /api/v1/leads/{leadId}/nurturing`
- `POST /api/v1/leads/{leadId}/nurturing/call-outcome`
- `POST /api/v1/leads/{leadId}/qualification`
- `POST /api/v1/leads/{leadId}/assign`
- `GET|POST /api/v1/leads/{leadId}/activities`
- `GET /api/v1/leads/{leadId}/activity` (compatibility alias)
- `GET|POST /api/v1/leads/{leadId}/tasks`
- `GET|POST /api/v1/tasks`
- `GET|PATCH /api/v1/tasks/{taskId}`
- `POST /api/v1/tasks/{taskId}/complete`
- `POST /api/v1/tasks/{taskId}/snooze`

Lead search accepts only `state`, `assignedAgentId`, `productLine`, `createdFrom`, `createdTo`, `search`, `cursor`, `limit`, and `sort` (`updatedAt`, `createdAt`, `name`).

## 8–12. Finance, documents, contracts, pre-launch, endorsement

- `GET|PATCH /api/v1/leads/{leadId}/down-payment`
- `POST /api/v1/leads/{leadId}/down-payment/generate-invoice`
- `GET /api/v1/leads/{leadId}/down-payment/invoice`
- `POST /api/v1/leads/{leadId}/down-payment/confirm`
- `GET /api/v1/finance/payment-queue`
- `GET /api/v1/leads/{leadId}/documents`
- `POST /api/v1/leads/{leadId}/documents/upload-intents`
  - Returns `documentId`, server-issued `objectKey`, a short-lived `uploadUrl`, required content type, expiry, and max size for the browser upload.
- `POST /api/v1/leads/{leadId}/documents/{documentId}/complete`
- `GET /api/v1/leads/{leadId}/documents/{documentId}/download-url`
- `POST /api/v1/leads/{leadId}/documents/{documentId}/archive`
- `GET|PATCH /api/v1/leads/{leadId}/contract`
- `POST /api/v1/leads/{leadId}/contract/generate`
- `POST /api/v1/leads/{leadId}/contract/submit-review`
- `GET|PUT /api/v1/leads/{leadId}/contract/review-checklist`
- `POST /api/v1/leads/{leadId}/contract/approve`
- `POST /api/v1/leads/{leadId}/contract/request-revision`
- `GET|POST /api/v1/leads/{leadId}/contract/signing-requests`
- `POST /api/v1/leads/{leadId}/contract/signing-requests/{requestId}/void`
- `GET /api/v1/public/contract-signing/{token}`
- `POST /api/v1/public/contract-signing/{token}/sign|decline`
- `GET /api/v1/gm/contract-review-queue`
- `GET /api/v1/leads/{leadId}/pre-launch` and `/items`
- `POST /api/v1/leads/{leadId}/pre-launch/initialize`
- `PATCH /api/v1/leads/{leadId}/pre-launch/items/{itemId}`
- `POST /api/v1/leads/{leadId}/pre-launch/send-video`
- `POST /api/v1/leads/{leadId}/pre-launch/complete`
- `GET|POST /api/v1/leads/{leadId}/endorsement`
- `GET /api/v1/endorsements` and `GET /api/v1/endorsements/{endorsementId}`
- `POST /api/v1/endorsements/{endorsementId}/acknowledge`
- `GET /api/v1/admin/endorsement-queue`

In Development without an S3 bucket, upload/download URLs are served by the signed local adapter. Production requires a private S3 bucket.

The required lifecycle is `NEW → INQUIRY → NURTURING → QUALIFIED/FOLLOW_UP → DOWNPAYMENT_PENDING → DOWNPAYMENT_CONFIRMED → CONTRACT_DRAFTING → CONTRACT_REVIEW/SIGNED → PRE_LAUNCH → ENDORSED_TO_ADMIN`. Location is captured as inquiry data; it is not a separate pipeline stage or approval gate.

## 13–15. Reports, notifications, audit, health

- `GET /api/v1/reports/overview|pipeline|conversion|goals|agent-leaderboard|down-payments`
- `GET /api/v1/notifications`
- `POST /api/v1/notifications/{notificationId}/read`
- `GET /api/v1/audit-logs`
- `GET /api/health/live`
- `GET /api/health/ready`

Business action endpoints use explicit transitions and object-level authorization. No generic state/approval/finance patch endpoint is exposed.
