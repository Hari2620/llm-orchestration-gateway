# Build context is ./guardrails_eval (see docker-compose.yml), but every module in
# this package uses absolute imports like "from guardrails_eval.checks... import ...",
# matching how it's imported in local dev (repo root on sys.path). So inside the
# image we nest the copied files under /app/guardrails_eval/ and run it as that
# package, rather than flattening it into /app directly.
FROM python:3.12-slim
WORKDIR /app

COPY . guardrails_eval/
RUN pip install --no-cache-dir -r guardrails_eval/requirements.txt

EXPOSE 8081
CMD ["uvicorn", "guardrails_eval.main:app", "--host", "0.0.0.0", "--port", "8081"]
