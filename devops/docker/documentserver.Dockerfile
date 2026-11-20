# OnlyOffice Docs with one change: the editors load their analytics module under a neutral file name.
#
# Ad and tracker blockers (uBlock Origin, AdGuard, Brave Shields, and web filters built on the same lists)
# block any URL ending in Analytics.js. The editors load web-apps/apps/common/Analytics.js as a required
# module, so when it is blocked the editor never starts and stays grey. The file is served unchanged under
# another name. See docs/decisions/0004-word-editing-with-onlyoffice.md.
#
# The build fails if a new OnlyOffice version loads the module differently, instead of shipping an editor
# that still breaks behind a blocker.
FROM onlyoffice/documentserver:9.4.0.1

RUN set -eu; \
    cd /var/www/onlyoffice/documentserver/web-apps/apps; \
    cp -p common/Analytics.js common/AppHooks.js; \
    loaders="$(grep -rl "'common/Analytics'" --include=app.js .)"; \
    [ -n "$loaders" ]; \
    sed -i "s#'common/Analytics'#'common/AppHooks'#" $loaders; \
    ! grep -rq "'common/Analytics'" --include=app.js .
