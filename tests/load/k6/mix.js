// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT
// Realistic mix of the four scenarios, weighted toward the cheap/cached routes as a typical
// gateway's traffic shape would be.
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const JWT_TOKEN = __ENV.JWT_TOKEN;
const API_KEY = __ENV.API_KEY;

export const options = {
  scenarios: {
    mix: {
      executor: 'constant-vus',
      vus: Number(__ENV.VUS || 100),
      duration: __ENV.DURATION || '60s',
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.02'],
  },
};

// Weights: bare 30%, cached 40%, jwt 20%, apiKey+rateLimit 10%.
function pickRoute() {
  const r = Math.random();
  if (r < 0.3) return () => http.get(`${BASE_URL}/bare/1`);
  if (r < 0.7) return () => http.get(`${BASE_URL}/cached/1`);
  if (r < 0.9) return () => http.get(`${BASE_URL}/secured/1`, { headers: { Authorization: `Bearer ${JWT_TOKEN}` } });
  return () =>
    http.get(`${BASE_URL}/limited/1`, {
      headers: { 'x-api-key': API_KEY },
      responseCallback: http.expectedStatuses(200, 429),
    });
}

export default function () {
  const res = pickRoute()();
  check(res, { 'status is 200 or 429': (r) => r.status === 200 || r.status === 429 });
}
