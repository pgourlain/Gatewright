# 03 - Shared rate limit with Redis

`rateLimit` with `"shared": true` gives one global budget to every gateway instance: each instance reserves small batches of tokens
from Redis and spends them locally (the hot path does not call Redis for each request).

```bash
docker compose up --build -d
# 10 requests per minute in total, whichever gateway receives them:
for i in $(seq 1 14); do curl -s -o /dev/null -w "%{http_code} " http://localhost:$((8080 + (i % 2) * 10))/orders; done; echo
```

You get `200` about ten times, then `429` on both gateways. Without Redis (remove `Redis__ConnectionString`) each gateway counts alone.
The limit can be exceeded by at most one batch per instance (`batchSize`). If Redis is down, `onStoreFailure` chooses `failOpen` (default) or `failClosed`.
