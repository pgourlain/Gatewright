// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT
// apiKey resolution (hashed lookup) + shared rateLimit (local token bucket, batched reservation
// in Redis, see D8). All VUs share one subscription on purpose, so the limit is actually exercised
// and the Redis reservation path (not just the local bucket) is on the hot path.
import http from 'k6/http';
import { check } from 'k6';
import { Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const API_KEY = __ENV.API_KEY;

const rateLimited = new Counter('rate_limited_responses');

export const options = {
  scenarios: {
    apikey_ratelimit: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 50),
      duration: __ENV.DURATION || '30s',
    },
  },
  thresholds: {
    // 429 is an expected outcome here, not a failure; only 5xx/network errors count.
    http_req_failed: ['rate<0.01'],
  },
};

export default function () {
  const res = http.get(`${BASE_URL}/limited/1`, {
    headers: { 'x-api-key': API_KEY },
    responseCallback: http.expectedStatuses(200, 429),
  });
  if (res.status === 429) {
    rateLimited.add(1);
  }
  check(res, { 'status is 200 or 429': (r) => r.status === 200 || r.status === 429 });
}
