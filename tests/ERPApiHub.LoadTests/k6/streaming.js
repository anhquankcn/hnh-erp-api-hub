import http from 'k6/http';
import { check } from 'k6';
import { baseUrl, taggedRequestParams } from './config.js';

export const options = {
  scenarios: {
    query_streaming: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 10),
      duration: __ENV.DURATION || '1m',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    'http_req_duration{name:query-stream}': ['p(95)<1000'],
    'http_req_receiving{name:query-stream}': ['p(95)<750'],
  },
};

export default function () {
  const urls = Array.from({ length: 3 }, (_, i) =>
    `${baseUrl}/api/v1/query/Customer/stream?pageSize=100&maxPages=3&filters=%5B%5B%22idx%22%2C%22%3E%3D%22%2C${i}%5D%5D`
  );

  const responses = http.batch(urls.map((url) => ['GET', url, null, taggedRequestParams('query-stream')]));

  for (const response of responses) {
    check(response, {
      'stream status is 200': (r) => r.status === 200,
      'stream returned content': (r) => r.body && r.body.length > 0,
    });
  }
}
