DOTNET ?= dotnet
ifneq (,$(wildcard .dotnet/dotnet))
DOTNET := ./.dotnet/dotnet
endif

SOLUTION := ForgeDesk.sln
CONFIGURATION ?= Release
PUBLISH_DIRECTORY := artifacts/publish/win-x64
PACKAGE_ARCHIVE := artifacts/ForgeDesk-win-x64.zip
FRESH_DATABASE := $(CURDIR)/artifacts/forgedesk-fresh.db

.PHONY: restore build test test-unit test-integration migrate-fresh publish package-release clean

restore:
	DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 $(DOTNET) restore $(SOLUTION) --locked-mode

build: restore
	DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 $(DOTNET) build $(SOLUTION) --configuration $(CONFIGURATION) --no-restore

test: build
	DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 $(DOTNET) test ForgeDesk.UnitTests/ForgeDesk.UnitTests.csproj --configuration $(CONFIGURATION) --no-build --no-restore --logger "console;verbosity=minimal"
	DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 $(DOTNET) test ForgeDesk.IntegrationTests/ForgeDesk.IntegrationTests.csproj --configuration $(CONFIGURATION) --no-build --no-restore --logger "console;verbosity=minimal"

test-unit: build
	DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 $(DOTNET) test ForgeDesk.UnitTests/ForgeDesk.UnitTests.csproj --configuration $(CONFIGURATION) --no-build --no-restore

test-integration: build
	DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 $(DOTNET) test ForgeDesk.IntegrationTests/ForgeDesk.IntegrationTests.csproj --configuration $(CONFIGURATION) --no-build --no-restore

migrate-fresh: build
	DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 $(DOTNET) tool restore
	mkdir -p artifacts
	rm -f "$(FRESH_DATABASE)" "$(FRESH_DATABASE)-shm" "$(FRESH_DATABASE)-wal"
	rm -f ForgeDesk.Infrastructure/artifacts/forgedesk-fresh.db ForgeDesk.Infrastructure/artifacts/forgedesk-fresh.db-shm ForgeDesk.Infrastructure/artifacts/forgedesk-fresh.db-wal
	FORGEDESK_DATABASE_PATH="$(FRESH_DATABASE)" DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 $(DOTNET) tool run dotnet-ef database update --project ForgeDesk.Infrastructure/ForgeDesk.Infrastructure.csproj --startup-project ForgeDesk.Infrastructure/ForgeDesk.Infrastructure.csproj

publish: build
	rm -rf "$(PUBLISH_DIRECTORY)"
	DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 $(DOTNET) publish ForgeDesk.App/ForgeDesk.App.csproj --configuration $(CONFIGURATION) --runtime win-x64 --self-contained true --output "$(PUBLISH_DIRECTORY)" --no-restore -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishAot=false

package-release: override CONFIGURATION := Release
package-release: publish
	rm -f "$(PACKAGE_ARCHIVE)"
	(cd "$(PUBLISH_DIRECTORY)" && zip -qr "../../ForgeDesk-win-x64.zip" .)
	test -s "$(PACKAGE_ARCHIVE)"

clean:
	DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 $(DOTNET) clean $(SOLUTION) --configuration $(CONFIGURATION)
