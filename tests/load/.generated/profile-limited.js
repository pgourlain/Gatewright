import http from 'k6/http';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const JWT_TOKEN = __ENV.JWT_TOKEN;

export const options = {
  scenarios: { limited: { executor: 'constant-vus', vus: Number(__ENV.VUS || 20), duration: __ENV.DURATION || '15s' } },
};

export default function () {
  const res = http.get(`${BASE_URL}/limited/1`, { headers: { Authorization: `Bearer ${JWT_TOKEN}` } });
  check(res, { 'status is 200': (r) => r.status === 200 });
}
