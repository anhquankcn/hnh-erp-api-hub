# Frappe Server Script Example - Webhook Event Push

## Overview

This script demonstrates how to push events from ERPNext/Frappe to the ERP API Hub via webhooks using Server Scripts.

## Setup

1. Go to **DocType** → **Server Script**
2. Create a new Server Script with:
   - **Script Type**: Event
   - **Doctype**: Sales Order (or any DocType)
   - **Event**: After Submit

## Script

```python
import frappe
import hashlib
import hmac
import json
import requests

WEBHOOK_URL = "https://erp-api-hub.example.com/api/v1/webhooks/erpnext"
WEBHOOK_SECRET = frappe.db.get_single_value("ERP API Hub Settings", "webhook_secret")

def push_event(doc, event_type):
    payload = {
        "event_type": event_type,
        "doctype": doc.doctype,
        "name": doc.name,
        "timestamp": frappe.utils.now(),
        "data": frappe.parse_json(doc.as_json())
    }
    
    body = json.dumps(payload, sort_keys=True)
    signature = hmac.new(
        WEBHOOK_SECRET.encode(),
        body.encode(),
        hashlib.sha256
    ).hexdigest()
    
    headers = {
        "Content-Type": "application/json",
        "X-ERPNext-Signature": f"sha256={signature}"
    }
    
    try:
        response = requests.post(
            WEBHOOK_URL,
            data=body,
            headers=headers,
            timeout=30
        )
        frappe.logger().info(f"Webhook pushed: {doc.name} - Status: {response.status_code}")
    except Exception as e:
        frappe.logger().error(f"Webhook failed: {doc.name} - {str(e)}")

# After Submit Event
push_event(doc, "sales_order.submitted")
```

## HMAC Signature Validation

The API Hub validates the signature using:

```csharp
var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body));
var actual = Convert.FromHexString(signature["sha256=".Length..]);
return CryptographicOperations.FixedTimeEquals(expected, actual);
```

## Events Supported

| Event | Description |
|-------|-------------|
| `sales_order.submitted` | Sales Order submitted |
| `sales_order.cancelled` | Sales Order cancelled |
| `customer.created` | Customer created |
| `customer.updated` | Customer updated |
| `invoice.submitted` | Sales Invoice submitted |

## Testing

```bash
curl -X POST https://erp-api-hub.example.com/api/v1/webhooks/erpnext \
  -H "Content-Type: application/json" \
  -H "X-ERPNext-Signature: sha256=<signature>" \
  -d '{"event_type":"sales_order.submitted","doctype":"Sales Order","name":"SO-2024-001","timestamp":"2024-01-01T00:00:00Z","data":{}}'
```
