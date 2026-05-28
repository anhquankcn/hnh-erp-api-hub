import http from 'k6/http';
import { check } from 'k6';
import { baseUrl, taggedRequestParams } from './config.js';

export const options = {
  scenarios: {
    cache_hits: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 25),
      duration: __ENV.DURATION || '1m',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{name:cache-hit}': ['p(95)<75'],
  },
};

const warmedUrls = Array.from({ length: 10 }, (_, i) =>
  `${baseUrl}/api/v1/query/Customer?page=1&pageSize=20&fields=%5B%22name%22%2C%22customer_name%22%5D&filters=%5B%5B%22idx%22%2C%22%3E%3D%22%2C${i}%5D%5D`
);

export function setup() {
  http.batch(warmedUrls.map((url) => ['GET', url, null, taggedRequestParams('cache-warm')]));
}

export default function () {
  const responses = http.batch(warmedUrls.map((url) => ['GET', url, null, taggedRequestParams('cache-hit')]));

  for (const response of responses) {
    check(response, {
      'cache hit status is 200': (r) => r.status === 200,
    });
  }
}
