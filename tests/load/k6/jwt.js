// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT
// JWT validation overhead: signature/claims check against the cached JWKS, no network call
// on the hot path (see JwtPolicy's HotPath_NeverFetchesJwks test).
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const JWT_TOKEN = __ENV.JWT_TOKEN;

export const options = {
  scenarios: {
    jwt: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 50),
      duration: __ENV.DURATION || '30s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
  },
};

export default function () {
  const res = http.get(`${BASE_URL}/secured/1`, {
    headers: { Authorization: `Bearer ${JWT_TOKEN}` },
  });
  check(res, { 'status is 200': (r) => r.status === 200 });
}
