import http from 'k6/http';
import { check } from 'k6';
import { baseUrl, taggedRequestParams } from './config.js';

export const options = {
  scenarios: {
    cache_misses: {
      executor: 'constant-arrival-rate',
      rate: Number(__ENV.RATE || 50),
      timeUnit: '1s',
      duration: __ENV.DURATION || '1m',
      preAllocatedVUs: Number(__ENV.VUS || 50),
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.02'],
    'http_req_duration{name:cache-miss}': ['p(95)<250'],
  },
};

export default function () {
  const nonce = `${__VU}-${__ITER}-${Date.now()}`;
  const urls = Array.from({ length: 5 }, (_, i) =>
    `${baseUrl}/api/v1/query/Customer?page=1&pageSize=20&filters=%5B%5B%22name%22%2C%22like%22%2C%22MISS-${nonce}-${i}%25%22%5D%5D`
  );

  const responses = http.batch(
    urls.map((url) => ['GET', url, null, taggedRequestParams('cache-miss', { 'Cache-Control': 'no-cache' })])
  );

  for (const response of responses) {
    check(response, {
      'cache miss status is 200': (r) => r.status === 200,
    });
  }
}
