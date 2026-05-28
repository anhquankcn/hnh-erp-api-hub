import http from 'k6/http';
import { check } from 'k6';
import { baseUrl, taggedRequestParams } from './config.js';

export const options = {
  scenarios: {
    ingestion_throughput: {
      executor: 'constant-arrival-rate',
      rate: Number(__ENV.RATE || 30),
      timeUnit: '1s',
      duration: __ENV.DURATION || '1m',
      preAllocatedVUs: Number(__ENV.VUS || 50),
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.02'],
    'http_req_duration{name:ingest-single}': ['p(95)<200'],
    'http_req_duration{name:ingest-batch}': ['p(95)<500'],
  },
};

export default function () {
  const suffix = `${__VU}-${__ITER}-${Date.now()}`;
  const singlePayload = JSON.stringify({
    payload: {
      name: `K6-CUST-${suffix}`,
      customer_name: `k6 Customer ${suffix}`,
      customer_type: 'Company',
    },
    name: `K6-CUST-${suffix}`,
  });

  const batchPayload = JSON.stringify(
    Array.from({ length: 10 }, (_, i) => ({
      doctype: 'Customer',
      payload: {
        name: `K6-BATCH-${suffix}-${i}`,
        customer_name: `k6 Batch Customer ${suffix}-${i}`,
        customer_type: 'Company',
      },
      name: `K6-BATCH-${suffix}-${i}`,
    }))
  );

  const responses = http.batch([
    ['POST', `${baseUrl}/api/v1/ingest/Customer`, singlePayload, taggedRequestParams('ingest-single')],
    ['POST', `${baseUrl}/api/v1/ingest/batch`, batchPayload, taggedRequestParams('ingest-batch')],
  ]);

  check(responses[0], {
    'single ingestion accepted': (r) => r.status === 202,
  });
  check(responses[1], {
    'batch ingestion accepted': (r) => r.status === 202,
  });
}
