// Copyright (c) Gatewright contributors.
// SPDX-License-Identifier: MIT
// OutputCache overhead and hit ratio: all VUs hit the same key, so the cache should converge
// to a near-100% hit rate after the first request per gateway instance.
import http from 'k6/http';
import { check } from 'k6';
import { Rate } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';

const cacheHitRate = new Rate('cache_hit_rate');

export const options = {
  scenarios: {
    cache: {
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
  const res = http.get(`${BASE_URL}/cached/1`);
  cacheHitRate.add(res.headers['X-Cache'] === 'HIT');
  check(res, { 'status is 200': (r) => r.status === 200 });
}
