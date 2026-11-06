// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT
// Baseline: no policy on the route. Measures the irreducible YARP + Gatewright pipeline overhead.
import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';

export const options = {
  scenarios: {
    bare_proxy: {
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
  const res = http.get(`${BASE_URL}/bare/1`);
  check(res, { 'status is 200': (r) => r.status === 200 });
}
