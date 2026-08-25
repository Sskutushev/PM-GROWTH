.PHONY: up down seed test verify quality
up:
	docker compose up -d --build
down:
	docker compose down
seed:
	curl -fsS -X POST http://localhost:8080/api/seed
test:
	dotnet test Timesheet.sln && npm --prefix frontend test
quality:
	python3 scripts/check-comment-language.py && dotnet build Timesheet.sln -warnaserror && npm --prefix frontend run typecheck && npm --prefix frontend run lint && npm --prefix frontend run test && npm --prefix frontend run build && npm --prefix frontend run format
verify: seed
	powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify.ps1
