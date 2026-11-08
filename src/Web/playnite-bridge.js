// Playnite Bridge web client for Jellyfin Web 10.10.7 and 12.1 (legacy layouts and the React library page, told apart at run time by markup and
// route, never by version number).
// Game pages: hides what a game cannot use and adds game-only rows to the item detail page. Games library: hides edit and play buttons, moves the
// server's filter entries (prefix "pb:") into their own groups, adds store icon markers and makes tiles view only.
(function () {
    'use strict';

    // Corners of the version marker (MarkerPosition); top left is the default.
    var corners = {
        TopRight: 'left:auto;right:0.3em',
        BottomLeft: 'top:auto;bottom:0.3em',
        BottomRight: 'top:auto;bottom:0.3em;left:auto;right:0.3em'
    };

    // The games views whose tiles are view only: the tabs of the games library except the server-wide Trailers tab (named by id, as data-index
    // differs per version), list.html grids and a game's "More like this". The React library page marks body instead (pbGamesTiles, see updateReact).
    var reactTiles = 'body.pbGamesTiles #moviesPage';
    var libraryScopes = ['moviesTab', 'suggestionsTab', 'favoritesTab', 'collectionsTab', 'genresTab'].map(function (id) {
        return '.pbGames #' + id;
    }).concat(['.pbGamesList', reactTiles]);
    var viewOnlyScopes = libraryScopes.concat(['#itemDetailPage.pbGame']);

    var style = document.createElement('style');
    style.id = 'pbStyles';
    var baseCss =
        '#itemDetailPage.pbGame .btnPlaystate,' +
        '#itemDetailPage.pbGame .btnUserRating,' +
        '#itemDetailPage.pbGame .btnMoreCommands,' +
        '#itemDetailPage.pbGame #castCollapsible,' +
        '#itemDetailPage.pbGame .itemMiscInfo-primary .endsAt,' +
        // Jellyfin's detail rows that DetailRows leaves out (hidden through classes on the view, see apply).
        '#itemDetailPage.pbGame.pbNoGenres .genresGroup,' +
        '#itemDetailPage.pbGame.pbNoDeveloper .directorsGroup,' +
        '#itemDetailPage.pbGame.pbNoPublisher .studiosGroup,' +
        // 12.1: the same rows are React rows in their own wrappers, marked by their place (metadataWrappers).
        '#itemDetailPage.pbGame.pbNoGenres .pbMetaGenre,' +
        '#itemDetailPage.pbGame.pbNoDeveloper .pbMetaDirector,' +
        '#itemDetailPage.pbGame.pbNoPublisher .pbMetaStudio,' +
        // 12.1's own Collections row, hidden while SeriesDisplay is Text.
        '#itemDetailPage.pbGame.pbNoCollections #collectionsCollapsible,' +
        // Jellyfin's More Like This row while ShowMoreLikeThis is off; the node stays, Jellyfin toggles its own hide class.
        '#itemDetailPage.pbGame.pbNoSimilar #similarCollapsible{display:none !important}' +
        // During an in-place version switch the row stays (old tiles) so the rows below do not jump.
        '#itemDetailPage.pbGame.pbKeepSimilar:not(.pbNoSimilar) #similarCollapsible.hide{display:block !important}' +
        // The camera and the line under the title on special feature cards while Show Video Icon / Show Subtext are off.
        '#itemDetailPage.pbGame.pbNoVideoIcon #specialsContent .cardIndicators,#itemDetailPage.pbGame.pbNoVideoIcon #pbSteamSpecialsCollapsible .cardIndicators,#itemDetailPage.pbGame.pbNoVideoIcon #pbVideoRowsCollapsible .cardIndicators,#itemDetailPage.pbGame.pbNoVideoIcon #pbVideoRowsBefore .cardIndicators,' +
        // A line that is off keeps its room while the other is on, so the row height does not change; both off remove the room.
        '#itemDetailPage.pbGame.pbNoTitle #specialsContent .cardText-first,#itemDetailPage.pbGame.pbNoTitle #pbSteamSpecialsCollapsible .cardText-first,#itemDetailPage.pbGame.pbNoTitle #pbVideoRowsCollapsible .cardText-first,#itemDetailPage.pbGame.pbNoTitle #pbVideoRowsBefore .cardText-first,' +
        '#itemDetailPage.pbGame.pbNoSubtext #specialsContent .cardText:not(.cardText-first),#itemDetailPage.pbGame.pbNoSubtext #pbSteamSpecialsCollapsible .cardText:not(.cardText-first),#itemDetailPage.pbGame.pbNoSubtext #pbVideoRowsCollapsible .cardText:not(.cardText-first),#itemDetailPage.pbGame.pbNoSubtext #pbVideoRowsBefore .cardText:not(.cardText-first){visibility:hidden}' +
        '#itemDetailPage.pbGame.pbNoTexts #specialsContent .cardText,#itemDetailPage.pbGame.pbNoTexts #pbSteamSpecialsCollapsible .cardText,#itemDetailPage.pbGame.pbNoTexts #pbVideoRowsCollapsible .cardText,#itemDetailPage.pbGame.pbNoTexts #pbVideoRowsBefore .cardText{display:none !important}' +
        // Picture Fit Blur Fill (pbFitBlur): ::after draws the whole picture, ::before the same picture enlarged to cover, blurred and darkened.
        '.cardImageContainer.pbFitBlur{overflow:hidden;background-size:0 0 !important}' +
        '.cardImageContainer.pbFitBlur::before,.cardImageContainer.pbFitBlur::after{content:"";position:absolute;top:0;right:0;bottom:0;left:0;background-image:inherit;background-position:center;background-repeat:no-repeat}' +
        '.cardImageContainer.pbFitBlur::before{background-size:cover;transform:scale(1.2);filter:blur(24px) brightness(.85)}' +
        '.cardImageContainer.pbFitBlur::after{background-size:contain}' +
        // The favourite heart of the video player while a game's extra plays (class pbExtraRating).
        '#videoOsdPage .btnUserRating.pbExtraRating{display:none !important}' +
        // The now-playing bar's heart while a soundtrack track plays; nothing is stored for a track.
        '.nowPlayingBarUserDataButtons .pbExtraRating,.nowPlayingBar.pbSoundtrackPlaying .btnToggleContextMenu{display:none !important}' +
        '.pbGames .btnNewCollection,' +
        // The Movies tab shows Shuffle once it has an item; it would play the games.
        '.pbGames .btnShuffle,' +
        // 12.1's Play All beside it.
        '.pbGames .btnPlayAll,' +
        '.pbGamesList .btnPlay,' +
        '.pbGamesList .btnShuffle{display:none !important}' +
        '.pbGamesFilter .pbHide,' +
        '.pbGamesFilter .videoTypeFilters,' +
        '.pbGamesFilter .seriesStatus,' +
        '.pbGamesFilter .episodeFilter,' +
        '.pbGamesFilter [data-settingname="IsResumable"],' +
        '.pbGamesFilter [data-settingname="VideoType"],' +
        '.pbGamesFilter [data-settingname="HasSubtitles"],' +
        '.pbGamesFilter [data-settingname="SeriesStatus"],' +
        // Jellyfin's group of local extras, shown only with a kind the games have (class pbExtrasShown, see showExtraFilters).
        '.pbGamesFilter .features:not(.pbExtrasShown),' +
        '.pbGamesFilter .featureSection:not(.pbExtrasShown){display:none !important}' +
        '.pbGamesFilter .features.pbExtrasShown,' +
        '.pbGamesFilter .featureSection.pbExtrasShown{display:block !important}' +
        // Store icon markers of a game tile: each badge is Jellyfin's .mediaSourceIndicator; the wrapper sets the position and the badges sit in a
        // row inside it.
        '.pbVersionMarker{display:flex;position:absolute;top:0.3em;left:0.3em;z-index:1}' +
        '.pbVersionMarker>.mediaSourceIndicator{position:static;margin-right:0.2em}' +
        '.pbVersionMarker>.mediaSourceIndicator:last-child{margin-right:0}' +
        '.pbVersionMarker img{width:1.3em;height:1.3em;object-fit:contain}' +
        // Top right beside Jellyfin's played check mark (pbBesidePlayed): the marker ends one icon gap left of it. Sizes are in em of the image
        // container; the number badge has an 88% font, hence the division by 0.88.
        '.pbBesidePlayed>.pbVersionMarker.pbTopRight{top:calc(0.225em + 0.8em - 0.88em);right:calc(0.225em + 1.6em + 0.176em)}' +
        // Link icons of the detail page (LinkDisplay Icon): 25 px high, as far apart as Jellyfin's ", " between text links.
        '#itemDetailPage.pbGame .itemExternalLinks .pbLinkIcon{margin:0 0.5em 0 0}' +
        '#itemDetailPage.pbGame .itemExternalLinks .pbLinkIcon:last-child{margin-right:0}' +
        '#itemDetailPage.pbGame .itemExternalLinks .pbLinkIcon img{display:block;height:25px;width:auto}' +
        // Detail page buttons (setActions): colour and hover stay Jellyfin's; the store logo sits in the glyph's corner with a round cut-out and is
        // drawn in currentColor as an SVG mask.
        '#itemDetailPage.pbGame .pbAction .detailButton-content{position:relative}' +
        '#itemDetailPage.pbGame .pbAction .detailButton-icon{font-size:24.7px !important;width:24.7px;height:24.7px;margin:.65px;position:relative;left:-5.5px;top:-5.5px;' +
        '-webkit-mask-image:radial-gradient(circle at 32.85px 28.85px,transparent 12px,#000 12px);mask-image:radial-gradient(circle at 32.85px 28.85px,transparent 12px,#000 12px)}' +
        '#itemDetailPage.pbGame .pbAction .detailButton-icon.open_in_new{font-size:21.85px !important;width:21.85px;height:21.85px;margin:2.075px;left:-6px;top:-6px;' +
        '-webkit-mask-image:radial-gradient(circle at 32.925px 28.925px,transparent 12px,#000 12px);mask-image:radial-gradient(circle at 32.925px 28.925px,transparent 12px,#000 12px)}' +
        '#itemDetailPage.pbGame .pbAction .detailButton-icon.computer{font-size:17.41px !important;width:17.41px;height:17.41px;margin:4.295px;left:-6.5px;top:-5.5px;' +
        '-webkit-mask-image:radial-gradient(circle at 31.205px 27.205px,transparent 12px,#000 12px);mask-image:radial-gradient(circle at 31.205px 27.205px,transparent 12px,#000 12px)}' +
        '#itemDetailPage.pbGame .pbActionLogo{position:absolute;left:15.925px;top:11.925px;width:16.15px;height:16.15px;background:currentColor;pointer-events:none;' +
        '-webkit-mask-size:contain;mask-size:contain;-webkit-mask-repeat:no-repeat;mask-repeat:no-repeat;-webkit-mask-position:center;mask-position:center}' +
        // DimUninstalled: shades the image of a tile whose main version is not installed. The shade sits over the container's background image;
        // markers and indicators stay on top. --pbDim = DimStrength / 100 on the view.
        '.pbUninstalled .cardScalable > .cardImageContainer::before,.pbUninstalled .listItemImage::before{content:"";position:absolute;' +
        'top:0;right:0;bottom:0;left:0;border-radius:inherit;background:rgba(0,0,0,var(--pbDim,.5));pointer-events:none}' +
        // React tiles draw the image as an <img> above that shade, so the image itself is darkened by the same amount; the shade rule does not match
        // React cards, so nothing is dimmed twice.
        '.pbUninstalled .cardContent > .cardImageContainer > div > img,.pbUninstalled .listItemImage > div > img{filter:brightness(calc(1 - var(--pbDim,.5)))}' +
        // 10.10.7 experimental: the toolbar inside the React page.
        'body.pbGamesReact #moviesPage .btnShuffle,body.pbGamesReact #moviesPage .btnNewCollection{display:none !important}';
    // 12.1 Modern: the library toolbar sits in the app bar outside #moviesPage; its buttons are found by their MUI icon. In the React filter popover
    // the groups a game cannot use and every server entry ("pb:...") in Jellyfin's Tags group are hidden (the entries are driven from the own groups,
    // see withReactTags). A browser without :has() drops only these rules.
    var reactFilter = 'body.pbGamesReact #filter-popover ';
    baseCss += ['PlayArrowIcon', 'ShuffleIcon', 'AddIcon', 'PlaylistAddIcon'].map(function (icon) {
        return 'body.pbGamesReact :has(> [aria-controls="library-view-menu"]) button:has(svg[data-testid="' + icon + '"])';
    }).concat(['filtersVideoTypes-header', 'filtersAudioLanguages-header', 'filtersSubtitleLanguages-header'].map(function (id) {
        return reactFilter + '> * > :has(#' + id + ')';
    }), ['IsResumable', 'HasSubtitles'].map(function (value) {
        return reactFilter + 'label:has(input[value="' + value + '"])';
    }), [reactFilter + '[aria-labelledby="filtersTags-header"] label:has(input[value^="pb:"])']).join(',') + '{display:none !important}';
    // While withReactTags opens Jellyfin's Tags group for a moment: its region stays hidden and no accordion animates.
    baseCss += 'body.pbTagsBusy #filter-popover [aria-labelledby="filtersTags-header"]{display:none !important}' +
        'body.pbTagsBusy #filter-popover > * > * *{transition:none !important}';
    // View only: the tile buttons Jellyfin draws are hidden on games views; the card link, hover shade, played indicator and markers stay. The play
    // button of a local extra, Steam Special Feature and soundtrack track comes back in a rule of its own (no complex :not(), which older TV browsers
    // drop with the whole rule).
    var tileButtons = [
        '.cardOverlayContainer .cardOverlayButton',
        '.cardOverlayButton[data-action="menu"]',
        '.btnCardOptions',
        '.listItem button[is="emby-playstatebutton"]',
        '.listItem button[is="emby-ratingbutton"]',
        '.listItem .listItemButton[data-action="menu"]'
    ];
    // Library views only: the mobile play button and the folder type indicator of collection tiles; a game page's extras keep both.
    var libraryTileParts = [
        '.cardOverlayButton[data-action="play"]',
        '.cardIndicators .videoIndicator'
    ];
    // The React tiles: every overlay button of a card and every button of a list item.
    var reactTileParts = ['.cardOverlayButton', '.listItemButton'];
    function scoped(scopes, selectors) {
        return scopes.map(function (scope) {
            return selectors.map(function (selector) {
                return scope + ' ' + selector;
            }).join(',');
        }).join(',');
    }
    baseCss += scoped(viewOnlyScopes, tileButtons) + ',' + scoped(libraryScopes, libraryTileParts) + ',' + scoped([reactTiles], reactTileParts) +
        '{display:none !important}' +
        '#itemDetailPage.pbGame #specialsContent .cardOverlayContainer > .cardOverlayFab-primary,' +
        '#itemDetailPage.pbGame #pbSteamSpecialsCollapsible .cardOverlayContainer > .cardOverlayFab-primary,#itemDetailPage.pbGame #pbVideoRowsCollapsible .cardOverlayContainer > .cardOverlayFab-primary,#itemDetailPage.pbGame #pbVideoRowsBefore .cardOverlayContainer > .cardOverlayFab-primary,' +
        '#itemDetailPage.pbGame #pbSoundtrackCollapsible .cardOverlayContainer > .cardOverlayFab-primary{display:inline-flex !important}' +
        // the three-dot menu button of a person card in the Cast row (real Jellyfin persons), as on Jellyfin's own cast row
        '#itemDetailPage.pbGame .card.personCard .cardOverlayContainer .cardOverlayButton[data-action="menu"]{display:inline-flex !important}';
    // A version switch on the standing page (class pbHold on html): the browser's picture of the old page stays on screen as a view transition until
    // the new page has settled, 1.5 s at most. Only the detail view is named for the transition, so the backdrop stays live. A browser without view
    // transitions drops only these rules.
    baseCss += 'html.pbHold #itemDetailPage{view-transition-name:pb-page}' +
        'html.pbHold .characterart-container:not(.artworkplus-outgoing){view-transition-name:ap-art}' +
        'html.pbHold::view-transition-old(root){display:none}' +
        'html.pbHold::view-transition-new(root){animation:none;opacity:1}' +
        'html.pbHold::view-transition-old(pb-page),html.pbHold::view-transition-old(ap-art){animation:pbHoldOld 1.5s step-end}' +
        'html.pbHold::view-transition-new(pb-page),html.pbHold::view-transition-new(ap-art){animation:pbHoldNew 1.5s step-end}' +
        '@keyframes pbHoldOld{from,to{opacity:1}}@keyframes pbHoldNew{from,to{opacity:0}}' +
        // the new cover is kept visible (important) while the old one's overlay fades out over it (keepCoverShown)
        'html #itemDetailPage .detailImageContainer .cardImageContainer.pbCoverShown{opacity:1 !important}';
    // Soundtrack row (loadSoundtrack): the album card opens and closes the tracks beside it; the loaded track has a ring, drawn inside the tile
    // (inset shadow of ::after) so no scroll position can cut it.
    baseCss += '#itemDetailPage.pbGame .pbAlbum button.cardImageContainer,#itemDetailPage.pbGame .pbTrack button.cardImageContainer{border:0;padding:0;width:100%;cursor:pointer}' +
        '#itemDetailPage.pbGame .pbAlbum button.cardImageContainer:not([class*="defaultCardBackground"]),#itemDetailPage.pbGame .pbTrack button.cardImageContainer:not([class*="defaultCardBackground"]){background-color:transparent}' +
        '#itemDetailPage.pbGame #pbSoundtrackCollapsible [is="emby-scroller"]{padding:.3em;margin:-.3em}' +
        '#itemDetailPage.pbGame .pbTrack.pbTrackLoaded .cardScalable::after{content:"";position:absolute;top:0;right:0;bottom:0;left:0;border-radius:inherit;' +
        'box-shadow:inset 0 0 0 .2em var(--theme-primary-color,#00a4dc);pointer-events:none}' +
        '#itemDetailPage.pbGame .pbTrackIn{animation:pbTrackIn .35s ease-out both}' +
        '@keyframes pbTrackIn{from{opacity:0;transform:translateX(-1.5em)}to{opacity:1;transform:none}}' +
        '@media (prefers-reduced-motion:reduce){#itemDetailPage.pbGame .pbTrackIn{animation:none}}';
    // Connections row (loadConnections): a thin vertical divider with the group's name stands before each group of the shared row.
    baseCss += '#itemDetailPage.pbGame .pbConnGroup{display:inline-block;flex:none;vertical-align:top;box-sizing:border-box;width:2em;height:auto;align-self:stretch;margin:0 .5em 0 .8em;padding-left:.4em;border-left:.1em solid rgba(255,255,255,.3);white-space:normal;position:relative;min-height:0}' +
        '#itemDetailPage.pbGame .pbConnGroup.pbShotGroup,#itemDetailPage.pbGame .pbConnGroup.pbSpecialGroup{width:1.8em}' +
        // the label hangs in the divider (absolute, cut with an ellipsis when long), so it must not make the divider taller than the tiles.
        '#itemDetailPage.pbGame .pbConnGroup span{position:absolute;top:0;left:.4em;display:block;writing-mode:vertical-rl;transform:rotate(180deg);height:100%;max-height:100%;text-align:end;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:.8em;opacity:.7}';
    // Screenshots (loadScreenshots): the "Show more" card at the end of a row is a dashed button over a card-sized box; a tile is a button of its own.
    baseCss += '#itemDetailPage.pbGame .pbShot{cursor:pointer}' +
        '#itemDetailPage.pbGame .pbShowMoreBtn{position:absolute;top:0;right:0;bottom:0;left:0;width:100%;border:.1em dashed rgba(255,255,255,.4);border-radius:.2em;' +
        'background:transparent;color:inherit;font:inherit;cursor:pointer}' +
        '#itemDetailPage.pbGame .pbShowMoreBtn:hover,#itemDetailPage.pbGame .pbShowMoreBtn:focus-visible{background:rgba(255,255,255,.1)}' +
        // keyboard focus on the Bridge's own tiles (screenshots and wallpapers, documents, the soundtrack album): a ring inside the tile, so a row that cuts overflow does not hide it
        '#itemDetailPage.pbGame .pbShot:focus-visible,#itemDetailPage.pbGame .pbDoc:focus-visible,#itemDetailPage.pbGame .pbAlbumButton:focus-visible{outline:.2em solid #fff;outline-offset:-.2em;box-shadow:inset 0 0 0 .4em rgba(0,0,0,.55)}';
    Object.keys(corners).forEach(function (corner) {
        baseCss += '.pbVersionMarker.pb' + corner + '{' + corners[corner] + '}';
    });
    style.textContent = baseCss;
    document.head.appendChild(style);

    // Number marker: Jellyfin draws .mediaSourceIndicator itself; on games views it moves to the chosen corner, and the list view's number badge
    // follows.
    function setMarkerStyle(config) {
        var corner = config ? corners[config.MarkerPosition] : null;
        var number = !!config && config.MarkerKind === 'Number';
        // Only in the view-only scopes: the Trailers tab keeps Jellyfin's place.
        var text = baseCss + (corner
            ? scoped(viewOnlyScopes, [number ? '.mediaSourceIndicator' : '.listItemImage>.mediaSourceIndicator']) + '{' + corner + '}'
            : '') + (corner && number && config.MarkerPosition === 'TopRight'
            ? scoped(viewOnlyScopes, ['.pbBesidePlayed>.mediaSourceIndicator'])
                + '{top:calc((0.225em + 0.8em - 0.88em) / 0.88);right:calc((0.225em + 1.6em + 0.176em) / 0.88)}'
            : '');
        if (style.textContent !== text) {
            style.textContent = text;
        }
    }

    // Own texts come from one table, PlayniteBridge/strings.json (served as playnite-bridge-strings.json), loaded once. The language is chosen like
    // globalize does (document language, exact variant, base language, else en-us). Words Jellyfin has itself (the "_jellyfin" keys) are read from
    // the language chunk Jellyfin has loaded (readJellyfin); a missing key or a failed read gives the own word.
    var tablePromise = null;

    function loadTable() {
        if (!tablePromise) {
            var api = window.ApiClient;
            var promise = api.getJSON(api.getUrl('PlayniteBridge/strings.json'));
            tablePromise = promise;
            promise.catch(function () {
                if (tablePromise === promise) {
                    tablePromise = null;
                }
            });
        }
        return tablePromise;
    }

    // runtime.bundle.js names the chunk of each language file ('<id>:"de-json"'); read once: code -> chunk id.
    var chunkIdsPromise = null;

    function chunkIds() {
        if (!chunkIdsPromise) {
            var runtime = document.querySelector('script[src*="runtime.bundle.js"]');
            var promise = runtime ? fetch(runtime.src).then(function (response) {
                return response.ok ? response.text() : Promise.reject(new Error('runtime.bundle.js'));
            }).then(function (text) {
                var ids = {};
                var pattern = /[{,](\d+):"([\w-]+)-json"/g;
                var match;
                while ((match = pattern.exec(text))) {
                    var code = match[2].replace('_', '-').toLowerCase();
                    // 12.1 ships an empty en_US.json beside en-us.json (src/strings): a name with "_" never displaces the one with "-".
                    if (!(code in ids) || match[2].indexOf('_') === -1) {
                        ids[code] = Number(match[1]);
                    }
                }
                return ids;
            }) : Promise.reject(new Error('runtime.bundle.js'));
            chunkIdsPromise = promise;
            promise.catch(function () {
                if (chunkIdsPromise === promise) {
                    chunkIdsPromise = null;
                }
            });
        }
        return chunkIdsPromise;
    }

    // Jellyfin's dictionary of the language (exact, then base) from the chunk globalize has loaded, read only; null when none is loaded.
    var jellyfinDictionaries = {};

    // Words key by key over the base language (he-il over he), so a regional file that lacks a key keeps the base word.
    function readJellyfin(ids, lang) {
        var chunks = window.webpackChunk;
        var codes = [lang, lang.replace(/-.*/, '')];
        var found = [];
        for (var c = 0; Array.isArray(chunks) && c < codes.length; c++) {
            var id = ids[codes[c]];
            if (id !== undefined && jellyfinDictionaries[id]) {
                found.push(jellyfinDictionaries[id]);
                continue;
            }
            search: for (var i = 0; id !== undefined && i < chunks.length; i++) {
                if (Array.isArray(chunks[i]) && Array.isArray(chunks[i][0]) && chunks[i][0].indexOf(id) !== -1) {
                    for (var key in chunks[i][1]) {
                        var source = String(chunks[i][1][key]);
                        var start = source.indexOf('JSON.parse(\'');
                        var end = source.lastIndexOf('\')');
                        if (start !== -1 && end > start) {
                            jellyfinDictionaries[id] = JSON.parse(source.substring(start + 12, end).replace(/\\([\\'])/g, '$1'));
                            found.push(jellyfinDictionaries[id]);
                            break search;
                        }
                    }
                }
            }
        }
        return found.length ? Object.assign.apply(null, [{}].concat(found.slice().reverse())) : null;
    }

    var lastStrings = null;

    function knownStrings() {
        var lang = (document.documentElement.getAttribute('lang') || '').toLowerCase();
        return lastStrings && lastStrings.lang === lang ? lastStrings.dictionary : null;
    }

    function loadStrings() {
        var lang = (document.documentElement.getAttribute('lang') || '').toLowerCase();
        return Promise.all([Promise.resolve().then(loadTable), chunkIds().catch(function () {
            return {};
        })]).then(function (results) {
            var table = results[0];
            var own = table[lang] || table[lang.replace(/-.*/, '')] || table['en-us'];
            var jellyfin = {};
            try {
                jellyfin = readJellyfin(results[1], lang) || {};
            } catch (e) {
            }
            var dictionary = {};
            Object.keys(own).forEach(function (key) {
                var word = table._jellyfin.indexOf(key) !== -1 && (own.jellyfinReplaced || []).indexOf(key) === -1 ? jellyfin[key] : null;
                // a Jellyfin word that lacks the {0} the own text needs (a person's role) would lose it: the own text counts
                if (typeof word === 'string' && /\{0\}/.test(own[key]) && !/\{0\}/.test(word)) {
                    word = null;
                }
                dictionary[key] = typeof word === 'string' && word ? word : own[key];
            });
            lastStrings = { lang: lang, dictionary: dictionary };
            return dictionary;
        }).catch(function () {
            // Without the table the texts stay empty and Jellyfin's own texts stay; the next call asks again.
            return {};
        });
    }

    // Jellyfin's date locale: the user's "Date and time format" setting; empty (Auto) means the UI language. Read only.
    function dateLocale() {
        var api = window.ApiClient;
        var userId = api && api.getCurrentUserId();
        var locale = userId ? localStorage.getItem(userId + '-datetimelocale') : null;
        return locale ? locale.replace('_', '-').toLowerCase() : document.documentElement.lang;
    }

    function formatDate(value) {
        var date = new Date(value);
        try {
            return date.toLocaleDateString(dateLocale() || undefined);
        } catch (e) {
            return date.toLocaleDateString();
        }
    }

    // Empty (never a key name) when the table could not be loaded.
    // The title an admin chose for a row or divider: a preset key is translated, Own is the custom text, default gives an empty string (callers keep
    // their own).
    function titleText(dictionary, configuration, base) {
        if (!configuration) {
            return '';
        }
        var names = { SpecialFeatures: 'SpecialFeaturesLabel', Screenshots: 'ScreenshotsLabel', Wallpapers: 'WallpapersLabel', Documents: 'DocumentsLabel', Soundtrack: 'SoundtrackLabel',
            SteamSpecialFeatures: 'SteamSpecialFeaturesLabel' };
        var key = configuration[base + 'Title'] || '';
        var own = configuration[names[base]] || '';
        if (key === 'Own') {
            return own;
        }
        return key ? translate(dictionary, key) || '' : '';
    }

    function translate(dictionary, key) {
        return dictionary[key] || '';
    }

    // Names the server sends in English that are shown in the UI language: Playnite's default completion statuses, the store names "Custom" and
    // "Unknown" and "Official Website". Compared exactly after trimming; every other name stays as it is.
    var serverNames = {
        status: {
            'Not Played': 'StatusNotPlayed',
            'Played': 'StatusPlayed',
            'Beaten': 'StatusBeaten',
            'Completed': 'StatusCompleted',
            'Playing': 'StatusPlaying',
            'Abandoned': 'StatusAbandoned',
            'On Hold': 'StatusOnHold',
            'Plan to Play': 'StatusPlanToPlay'
        },
        store: { 'Custom': 'PlatformCustom', 'Unknown': 'Unknown' },
        link: { 'Official Website': 'OfficialWebsite' }
    };

    function serverName(dictionary, kind, name) {
        var trimmed = String(name).trim();
        var key = Object.prototype.hasOwnProperty.call(serverNames[kind], trimmed) ? serverNames[kind][trimmed] : null;
        return key && translate(dictionary, key) || name;
    }

    function versionLabel(dictionary, label) {
        var slash = label.indexOf('/');
        return slash === -1 ? serverName(dictionary, 'store', label) : serverName(dictionary, 'store', label.substring(0, slash)) + label.substring(slash);
    }

    function markerTitle(dictionary, title) {
        return title.split(', ').map(function (label) {
            return versionLabel(dictionary, label);
        }).join(', ');
    }

    function itemId() {
        var hash = window.location.hash;
        return /^#\/details\?/.test(hash) ? new URLSearchParams(hash.substring(hash.indexOf('?') + 1)).get('id') : null;
    }

    // A URL that carries the access token (ApiKey) in its query for requests the page cannot add a header to (a download for pdf.js, an audio or
    // video element). 12.x ignores api_key unless EnableLegacyAuthorization is on.
    function tokenUrl(api, path, params) {
        var query = params || {};
        query.ApiKey = api.accessToken();
        return api.getUrl(path, query);
    }

    function readableSize(size) {
        if (size >= 1073741824) {
            return parseFloat((size / 1073741824).toFixed(1)) + ' GiB';
        } else if (size >= 1048576) {
            return parseFloat((size / 1048576).toFixed(1)) + ' MiB';
        }
        return Math.floor(size / 1024) + ' KiB';
    }

    // The classes emby-button's createdCallback would add, written with the markup so the element never changes class after it is drawn (that would
    // start a transition).
    function upgradedClasses() {
        return ' emby-button' + (document.documentElement.classList.contains('layout-tv') ? ' show-focus' : '');
    }

    function row(cssClass, label, parts) {
        var item = document.createElement('div');
        item.className = 'detailsGroupItem pbRow ' + cssClass;
        var labelElem = document.createElement('div');
        labelElem.className = 'label';
        labelElem.textContent = label;
        var contentElem = document.createElement('div');
        contentElem.className = 'content focuscontainer-x';
        contentElem.innerHTML = parts.map(function (part) {
            return part.href
                ? '<a style="color:inherit;" class="button-link' + upgradedClasses() + '" is="emby-linkbutton" href="' + escapeHtml(part.href) + '">' + escapeHtml(part.name) + '</a>'
                : escapeHtml(part.name);
        }).join(', ');
        item.appendChild(labelElem);
        item.appendChild(contentElem);
        return item;
    }

    function removeRows(view) {
        var rows = view.querySelectorAll('.pbRow, .pbAction, #pbCollectionsCollapsible, #pbSteamSpecialsCollapsible, #pbVideoRowsBefore, #pbVideoRowsCollapsible, #pbScreenshotsCollapsible, #pbWallpapersCollapsible, #pbDocumentsCollapsible, #pbSoundtrackCollapsible, #pbRelatedCollapsible, #pbActorsCollapsible, #pbConnectionsCollapsible, .detailImageContainer .pbVersionMarker');
        for (var i = 0; i < rows.length; i++) {
            rows[i].parentNode.removeChild(rows[i]);
        }
    }

    function setText(elem, text) {
        if (elem && text && elem.textContent !== text) {
            elem.textContent = text;
        }
    }

    function sameRow(elem, spec) {
        if (!elem || !elem.classList.contains('pbRow') || !elem.classList.contains(spec[0]) || elem.querySelector('.label').textContent !== spec[1]) {
            return false;
        }
        // A 12.1 row (metadataRow) has its content box last, without the class content.
        var content = elem.querySelector('.content') || elem.lastElementChild;
        var links = content.querySelectorAll('a');
        var hrefs = spec[2].filter(function (part) {
            return part.href;
        });
        var same = content.textContent === spec[2].map(function (part) {
            return part.name;
        }).join(', ') && links.length === hrefs.length;
        for (var i = 0; same && i < links.length; i++) {
            same = links[i].getAttribute('href') === hrefs[i].href;
        }
        return same;
    }

    // The href of a genre, developer, publisher or tag link that opens the games library's list grid; null if the link carries no id.
    function gameHref(kind, href, libraryId, route) {
        var query = new URLSearchParams(href && href.indexOf('?') !== -1 ? href.substring(href.indexOf('?') + 1) : '');
        var tail = '&serverId=' + encodeURIComponent(query.get('serverId') || '') + '&parentId=' + libraryId;
        var id;
        if (kind === 'genre') {
            id = query.get('genreId');
            return id ? route + 'genreId=' + encodeURIComponent(id) + tail : null;
        }
        if (kind === 'developer') {
            id = query.get('personId') || query.get('id');
            return id ? route + 'personId=' + encodeURIComponent(id) + tail + '&type=Movie' : null;
        }
        if (kind === 'publisher') {
            id = query.get('studioId');
            return id ? route + 'studioId=' + encodeURIComponent(id) + tail : null;
        }
        id = query.get('tag');
        return id !== null ? route + 'type=tag&tag=' + encodeURIComponent(id) + tail : null;
    }

    // The start of the #/list links the script writes: '#/list?' on 12.1, '#/list.html?' on 10.10.7, decided by the current route, else by the detail
    // page markup. Routes are always read in both forms (routeParam).
    function listRoute(view) {
        var page = (/^#\/([^?]*)/.exec(window.location.hash) || ['', ''])[1];
        if (page === 'movies' || page === 'list') {
            return '#/list?';
        }
        if (page === 'movies.html' || page === 'list.html') {
            return '#/list.html?';
        }
        var group = view ? view.querySelector('.itemDetailsGroup') : null;
        return view && (view.querySelector('.detailImageContainer.hide-mobile') || group && group.firstElementChild && !group.querySelector('.genresGroup'))
            ? '#/list?' : '#/list.html?';
    }

    function setAttribute(elem, name, value) {
        if (elem.getAttribute(name) !== value) {
            elem.setAttribute(name, value);
        }
    }

    function setHidden(elem, hidden) {
        if (elem.classList.contains('hide') !== hidden) {
            elem.classList.toggle('hide', hidden);
        }
    }

    function displayDuration(ticks) {
        var totalMinutes = Math.round(ticks / 600000000) || 1;
        var hours = Math.floor(totalMinutes / 60);
        return (hours > 0 ? hours + 'h ' : '') + (totalMinutes % 60) + 'm';
    }

    // The runtime entry of the first info line shows the chosen version's playtime; none at 0.
    function setRuntime(view, ticks) {
        var text = ticks ? displayDuration(ticks) : null;
        var lines = view.querySelectorAll('.itemMiscInfo-primary');
        for (var l = 0; l < lines.length; l++) {
            var items = lines[l].querySelectorAll('.mediaInfoItem');
            var current = null;
            var year = null;
            for (var i = 0; i < items.length; i++) {
                if (items[i].className === 'mediaInfoItem' && /^(\d+h )?\d+m$/.test(items[i].textContent)) {
                    current = items[i];
                } else if (items[i].className === 'mediaInfoItem' && /^\d{4}$/.test(items[i].textContent)) {
                    year = items[i];
                }
            }
            if (!text) {
                if (current) {
                    current.parentNode.removeChild(current);
                }
            } else if (current) {
                setText(current, text);
            } else {
                var runtime = document.createElement('div');
                runtime.className = 'mediaInfoItem';
                runtime.textContent = text;
                lines[l].insertBefore(runtime, year ? year.nextSibling : lines[l].firstChild);
                setHidden(lines[l], false);
            }
        }
    }

    function mediaInfoPart(item) {
        var classes = item.classList;
        if (classes.contains('mediaInfoOfficialRating')) {
            return 'AgeRating';
        }
        if (classes.contains('starRatingContainer')) {
            return 'CommunityRating';
        }
        if (classes.contains('mediaInfoCriticRating')) {
            return 'CriticRating';
        }
        return (' ' + item.className + ' ').replace(' hide ', ' ').trim() === 'mediaInfoItem' && /^\d{4}$/.test(item.textContent) ? 'Year' : null;
    }

    // The parts of the first info line that MediaInfo leaves out are hidden; a line without a visible part is hidden too (.endsAt is always hidden on
    // game pages).
    function setMediaInfo(view, parts) {
        var lines = view.querySelectorAll('.itemMiscInfo-primary');
        for (var l = 0; l < lines.length; l++) {
            var items = lines[l].querySelectorAll('.mediaInfoItem');
            var shown = false;
            for (var i = 0; i < items.length; i++) {
                var part = mediaInfoPart(items[i]);
                if (part) {
                    setHidden(items[i], parts.indexOf(part) === -1);
                }
                shown = shown || !(items[i].classList.contains('hide') || items[i].classList.contains('endsAt'));
            }
            setHidden(lines[l], !shown);
        }
    }

    // The external links of the chosen version in Jellyfin's markup (renderLinks); Jellyfin draws none in the TV layout. A link with an icon key is
    // drawn as its icon only with the name as tooltip, and compared by href, title and key. The name is shown translated (serverName).
    function setLinks(view, links, dictionary) {
        var elem = view.querySelector('.itemExternalLinks');
        if (!elem || document.documentElement.classList.contains('layout-tv')) {
            return;
        }
        var names = links.map(function (link) {
            return serverName(dictionary, 'link', link.name);
        });
        var anchors = elem.querySelectorAll('a');
        var same = anchors.length === links.length;
        for (var i = 0; same && i < links.length; i++) {
            same = anchors[i].getAttribute('href') === links[i].url && (links[i].icon
                ? anchors[i].getAttribute('data-pb-icon') === links[i].icon && anchors[i].getAttribute('title') === names[i]
                : anchors[i].textContent === names[i] && !anchors[i].hasAttribute('data-pb-icon'));
        }
        if (!same) {
            elem.innerHTML = links.map(function (link, n) {
                if (link.icon) {
                    return '<a is="emby-linkbutton" class="button-link' + upgradedClasses() + ' pbLinkIcon" href="' + escapeHtml(link.url) + '" target="_blank" title="' +
                        escapeHtml(names[n]) + '" data-pb-icon="' + escapeHtml(link.icon) + '"><img src="' +
                        escapeHtml(window.ApiClient.getUrl('PlayniteBridge/link-icons/' + link.icon)) + '" alt="' + escapeHtml(names[n]) + '"></a>';
                }
                return '<a is="emby-linkbutton" class="button-link' + upgradedClasses() + '" href="' + escapeHtml(link.url) + '" target="_blank">' + escapeHtml(names[n]) + '</a>';
            }).join(links.some(function (link) { return link.icon; }) ? '' : ', ');
        }
        setHidden(elem, !links.length);
    }

    // Jellyfin's "Version" list (.trackSelections), which Jellyfin leaves hidden for a virtual item; the labels' store part is translated
    // (versionLabel).
    function setVersions(view, game, version, dictionary) {
        var form = view.querySelector('.trackSelections');
        var container = view.querySelector('.selectSourceContainer');
        var select = view.querySelector('.selectSource');
        if (!form || !container || !select || game.versions.length < 2) {
            return;
        }
        ['.selectVideoContainer', '.selectAudioContainer', '.selectSubtitlesContainer'].forEach(function (selector) {
            var other = view.querySelector(selector);
            if (other) {
                setHidden(other, true);
            }
        });
        var same = select.options.length === game.versions.length;
        for (var i = 0; same && i < game.versions.length; i++) {
            same = select.options[i].value === game.versions[i].id && select.options[i].textContent === versionLabel(dictionary, game.versions[i].label);
        }
        if (!same) {
            select.innerHTML = game.versions.map(function (v) {
                return '<option value="' + v.id + '">' + escapeHtml(versionLabel(dictionary, v.label)) + '</option>';
            }).join('');
        }
        var wanted = view.pbPending && game.versions.some(function (v) {
            return v.id === view.pbPending;
        }) ? view.pbPending : version.id;
        if (select.value !== wanted) {
            select.value = wanted;
        }
        var label = container.querySelector('label');
        if (label && select.setLabel && translate(dictionary, 'LabelVersion') && (!same || label.textContent !== translate(dictionary, 'LabelVersion'))) {
            select.setLabel(translate(dictionary, 'LabelVersion'));
        }
        setHidden(form, false);
        setHidden(container, false);
    }

    // A detail page button: via Link the browser opens the URI, via Server the plugin starts it on the PC (the server decides again). Locked until
    // the answer.
    function onAction(e) {
        var button = e.currentTarget;
        var action = button.pbAction;
        if (!action || button.disabled) {
            return;
        }
        if (action.via === 'Link') {
            // A temporary link hands the URI to the browser without navigating the page away.
            var link = document.createElement('a');
            link.href = action.uri;
            document.body.appendChild(link);
            link.click();
            link.remove();
            return;
        }
        var api = window.ApiClient;
        var url = api.getUrl('PlayniteBridge/games/' + button.pbVersionId + '/action/' + action.kind);
        button.disabled = true;
        api.ajax({ type: 'POST', url: url }).then(function () {
            button.disabled = false;
        }, function () {
            button.disabled = false;
            loadStrings().then(function (dictionary) {
                window.Dashboard.alert(translate(dictionary, 'StartFailed'));
            });
        });
    }

    var actionGlyphs = {
        DirectStart: 'play_arrow',
        StartViaPlaynite: 'play_arrow',
        OpenInPlaynite: 'open_in_new',
        InstallDirect: 'get_app',
        InstallViaPlaynite: 'get_app'
    };

    // The buttons of the chosen version (games/{id} actions, in page order) in the markup of .mainDetailButtons, right before .btnPlay. Each shows
    // the action's glyph with the store or Playnite logo in front; a remote action runs on the PC and shows a computer glyph.
    function setActions(view, version, dictionary) {
        var specs = (version.actions || []).map(function (action) {
            var remote = action.via === 'Server' && action.remote;
            return {
                action: action,
                kind: 'pbAction-' + action.kind,
                glyph: remote ? 'computer' : actionGlyphs[action.kind],
                logo: action.logo,
                title: translate(dictionary, action.kind + (remote ? 'OnPc' : ''))
            };
        });
        var holders = view.querySelectorAll('.mainDetailButtons');
        for (var h = 0; h < holders.length; h++) {
            var play = holders[h].querySelector('.btnPlay');
            var buttons = holders[h].querySelectorAll('.pbAction');
            var same = buttons.length === specs.length && (!specs.length || buttons[buttons.length - 1].nextElementSibling === play);
            for (var i = 0; same && i < specs.length; i++) {
                same = buttons[i].classList.contains(specs[i].kind) && buttons[i].title === specs[i].title
                    && buttons[i].getAttribute('data-pb-logo') === specs[i].logo && !!buttons[i].querySelector('.detailButton-icon.' + specs[i].glyph);
            }
            if (!same) {
                for (var r = 0; r < buttons.length; r++) {
                    buttons[r].parentNode.removeChild(buttons[r]);
                }
                var holder = document.createElement('div');
                holder.innerHTML = specs.map(function (spec) {
                    var mask = 'url(' + window.ApiClient.getUrl('PlayniteBridge/action-logos/' + spec.logo) + ')';
                    return '<button is="emby-button" type="button" class="button-flat detailButton pbAction ' + spec.kind + upgradedClasses() + '" data-pb-logo="' + escapeHtml(spec.logo)
                        + '" title="' + escapeHtml(spec.title) + '" aria-label="' + escapeHtml(spec.title) + '"><div class="detailButton-content">'
                        + '<span class="material-icons detailButton-icon ' + spec.glyph + '" aria-hidden="true"></span>'
                        + '<span class="pbActionLogo" style="' + escapeHtml('-webkit-mask-image:' + mask + ';mask-image:' + mask) + '" aria-hidden="true"></span>'
                        + '</div></button>';
                }).join('');
                while (holder.firstChild) {
                    holder.firstChild.addEventListener('click', onAction);
                    holders[h].insertBefore(holder.firstChild, play);
                }
                buttons = holders[h].querySelectorAll('.pbAction');
            }
            for (var b = 0; b < specs.length; b++) {
                buttons[b].pbAction = specs[b].action;
                buttons[b].pbVersionId = version.id;
            }
        }
    }

    // The store icon marker on the cover of the detail page: the same marker as the game's tile, in the MarkerPosition corner. Applied to every
    // cover, as 12.1 draws two (desktop/TV and phone).
    function setDetailMarker(view, marker, position, dictionary) {
        var holders = view.querySelectorAll('.detailImageContainer .cardImageContainer');
        position = marker && marker.position || position;
        var shown = marker && (marker.icons.length ? marker.icons : marker.number ? [String(marker.number)] : []);
        for (var i = 0; i < holders.length; i++) {
            // A version without a cover: Jellyfin's image box stays hidden with data-src "null", so the marker sits on the card's own box
            // (.cardScalable).
            var src = holders[i].getAttribute('data-src');
            var bare = (src === 'null' || src === 'undefined') && holders[i].parentNode;
            var holder = bare ? holders[i].parentNode : holders[i];
            var stale = bare ? holders[i].querySelector('.pbVersionMarker') : holders[i].parentNode && holders[i].parentNode.querySelector(':scope > .pbVersionMarker');
            if (stale) {
                stale.parentNode.removeChild(stale);
            }
            if (shown && shown.length && position !== 'Off') {
                setMarker(holder, position, shown, markerTitle(dictionary, marker.title));
            } else {
                var old = holder.querySelector(':scope > .pbVersionMarker');
                if (old) {
                    old.parentNode.removeChild(old);
                }
            }
        }
    }

    function currentVersion(view, game) {
        return game.versions.filter(function (v) {
            return v.id === view.pbVersion;
        })[0] || game.versions[0];
    }

    // 12.1 renderDetails empties .itemDetailsGroup and appends one div per metadata type (Author, Creator, Director, Writer, Studio, Genre). null
    // while the group does not hold exactly these six divs besides our rows (10.10.7, or 12.1 before renderDetails).
    var metadataClasses = { 2: 'pbMetaDirector', 4: 'pbMetaStudio', 5: 'pbMetaGenre' };

    function metadataWrappers(group) {
        var divs = group ? [].filter.call(group.children, function (child) {
            return !child.classList.contains('pbRow');
        }) : [];
        if (divs.length !== 6 || divs.some(function (div) {
            return div.tagName !== 'DIV';
        })) {
            return null;
        }
        Object.keys(metadataClasses).forEach(function (index) {
            if (!divs[index].classList.contains(metadataClasses[index])) {
                divs[index].classList.add(metadataClasses[index]);
            }
        });
        return divs;
    }

    function setLabelNode(wrapper, text) {
        var label = wrapper.querySelector('.detailsGroupItem > .label');
        var node = label ? label.firstChild : null;
        if (text && node && node.nodeType === Node.TEXT_NODE && !node.nextSibling && node.nodeValue !== text) {
            node.nodeValue = text;
        }
    }

    // An own row in the markup of ItemDetailsMetadataList: tag names and classes are copied from a row Jellyfin drew on this page, so font, spacing
    // and label column are Jellyfin's. Without such a row the markup of row() is used. spec: [class, label, parts].
    function metadataRow(template, spec) {
        var label = template && template.firstElementChild;
        var box = template && template.lastElementChild;
        if (!label || label === box) {
            return row(spec[0], spec[1], spec[2]);
        }
        var span = box.firstElementChild;
        var item = document.createElement(template.tagName);
        item.className = template.className + ' pbRow ' + spec[0];
        var labelElem = document.createElement(label.tagName);
        labelElem.className = label.className;
        labelElem.textContent = spec[1];
        var contentElem = document.createElement(box.tagName);
        contentElem.className = box.className;
        contentElem.innerHTML = spec[2].map(function (part, i) {
            return '<span' + (span ? ' class="' + escapeHtml(span.className) + '"' : '') + '>' + (i ? ', ' : '') + (part.href
                ? '<a style="color:inherit;" class="button-link' + upgradedClasses() + '" is="emby-linkbutton" href="' + escapeHtml(part.href) + '">' + escapeHtml(part.name) + '</a>'
                : escapeHtml(part.name)) + '</span>';
        }).join('');
        item.appendChild(labelElem);
        item.appendChild(contentElem);
        return item;
    }

    // The rows of 12.1: the series row before Jellyfin's block, Jellyfin's rows in 12.1's order (developers in the director's row, publishers in the
    // studio's), then platform, features, size and status. Our rows are rewritten only when one differs, so the observer of watch starts no loop.
    function metadataRows(group, wrappers, before, after, dictionary) {
        var first = wrappers[0];
        var last = wrappers[wrappers.length - 1];
        var rowsOk = group.querySelectorAll('.pbRow').length === before.length + after.length
            && (!before.length || sameRow(first.previousElementSibling, before[0]));
        var next = last.nextElementSibling;
        for (var i = 0; rowsOk && i < after.length; i++) {
            rowsOk = sameRow(next, after[i]);
            next = next && next.nextElementSibling;
        }
        if (!rowsOk) {
            var rows = group.querySelectorAll('.pbRow');
            for (var r = 0; r < rows.length; r++) {
                rows[r].parentNode.removeChild(rows[r]);
            }
            var template = group.querySelector('.detailsGroupItem:not(.pbRow)');
            if (before.length) {
                group.insertBefore(metadataRow(template, before[0]), first);
            }
            var at = last;
            after.forEach(function (spec) {
                var elem = metadataRow(template, spec);
                group.insertBefore(elem, at.nextSibling);
                at = elem;
            });
        }
        setLabelNode(wrappers[2], translate(dictionary, wrappers[2].querySelectorAll('a').length > 1 ? 'Developers' : 'LabelDeveloper'));
        setLabelNode(wrappers[4], translate(dictionary, wrappers[4].querySelectorAll('a').length > 1 ? 'Publishers' : 'Publisher'));
    }

    // 12.1: a link of the detail rows or tags opens the route its href attribute holds. LinkButton shows its React href prop instead, so the click is
    // taken first here (capture phase) and shown through appRouter.show; a click with a modifier key is left to the browser.
    function onDetailLink(e) {
        var view = this;
        var link = e.target && e.target.closest ? e.target.closest('a[href]') : null;
        var href = link ? link.getAttribute('href') : '';
        if (!link || !view.classList.contains('pbGame') || !view.pbRedo || view.querySelector('.genresGroup') || !link.closest('.itemDetailsGroup, .itemTags')
            || !/^#\/list(\.html)?\?/.test(href) || !window.Emby || !window.Emby.Page) {
            return;
        }
        e.stopPropagation();
        if (!(e.ctrlKey || e.metaKey || e.shiftKey || e.altKey)) {
            e.preventDefault();
            Promise.resolve(window.Emby.Page.show(href)).catch(function () {
            });
        }
    }

    // Brings the game's own state onto the page using the chosen version. Writes only what differs, so it can run again after every change Jellyfin
    // makes without starting a loop. display: the page settings from ClientConfiguration.
    function apply(view, game, dictionary, libraryId, display) {
        var version = currentVersion(view, game);
        setAttribute(view, 'data-pb-version', version.id);
        setAttribute(view, 'data-pb-installed', String(version.installed));
        setAttribute(view, 'data-pb-store', version.store);
        setVersions(view, game, version, dictionary);
        setRuntime(view, display.mediaInfo.indexOf('Playtime') !== -1 ? version.playtime : 0);
        setMediaInfo(view, display.mediaInfo);
        setLinks(view, version.links, dictionary);
        setActions(view, version, dictionary);
        setDetailMarker(view, game.marker, display.markerPosition, dictionary);
        orderRows(view, display.rowOrder);
        orderFlex(view, display.rowOrder);
        view.classList.toggle('pbNoCollections', display.series === 'Text' || display.series === 'Off');
        view.classList.toggle('pbNoSimilar', !display.showSimilar);
        view.classList.toggle('pbNoVideoIcon', !display.showVideoIcon);
        view.classList.toggle('pbNoSubtext', !display.showSubtext);
        view.classList.toggle('pbNoTitle', !display.showTitle);
        view.classList.toggle('pbNoTexts', !display.showTitle && !display.showSubtext);
        textOptions = { docFit: display.docFit, docLimit: display.docLimit, docTitle: display.docTitle, docSubtext: display.docSubtext, docThumbs: display.docThumbs, serverComics: display.serverComics, shotFit: display.shotFit, shotLimit: display.shotLimit, shotTitle: display.shotTitle, shotSubtext: display.shotSubtext,
            wpFit: display.wpFit, wpLimit: display.wpLimit, wpTitle: display.wpTitle, wpSubtext: display.wpSubtext };
        labelOptions = display.labels;
        steamLabel = display.steamLabel || translate(dictionary, 'SteamSpecialFeatures');
        steamDictionary = dictionary;

        var group = view.querySelector('.itemDetailsGroup');
        var genres = view.querySelector('.genresGroup');
        var studios = view.querySelector('.studiosGroup');
        var route = listRoute(view);
        // A series with a collection opens it; a source, feature or completion status opens the games grid filtered by its filter entry. Names are
        // shown translated (serverName), the link keeps the raw filter id.
        var serverId = encodeURIComponent(routeParam('details', 'serverId') || window.ApiClient.serverId());
        function filterLink(entry, kind) {
            return { name: kind ? serverName(dictionary, kind, entry.name) : entry.name, href: libraryId ? route + 'genreId=' + entry.filterId + '&serverId=' + serverId + '&parentId=' + libraryId : null };
        }
        function shown(row) {
            return display.detailRows.indexOf(row) !== -1;
        }
        [['pbNoGenres', 'Genres'], ['pbNoDeveloper', 'Developer'], ['pbNoPublisher', 'Publisher']].forEach(function (entry) {
            view.classList.toggle(entry[0], !shown(entry[1]));
        });
        var before = display.series !== 'Tiles' && display.series !== 'Off' && shown('Series') && game.series.length ? [['pbSeriesGroup', display.seriesTerm || translate(dictionary, 'Series'), game.series.map(function (series) {
            return { name: series.name, href: series.collectionId ? route + 'parentId=' + series.collectionId + '&serverId=' + serverId : null };
        })]] : [];
        // The group's sources (each store once, in platform list order) on every game page, right after studios.
        var after = shown('Platform') ? [['pbSourceGroup', translate(dictionary, 'Platform'), game.sources.map(function (source) {
            return filterLink(source, 'store');
        })]] : [];
        if (shown('Features') && game.features.length) {
            after.push(['pbFeaturesGroup', translate(dictionary, 'Features'), game.features.map(function (feature) {
                return filterLink(feature, null);
            })]);
        }
        if (shown('Size') && version.installSize) {
            after.push(['pbInstallSizeGroup', translate(dictionary, 'LabelSize'), [{ name: readableSize(version.installSize), href: null }]]);
        }
        if (shown('Status') && version.completionStatus) {
            after.push(['pbCompletionGroup', translate(dictionary, 'LabelStatus'), [filterLink(version.completionStatus, 'status')]]);
        }

        // 12.1: Jellyfin's rows are React rows (metadataRows); without its wrappers yet (before renderDetails) no row is touched.
        var wrappers = genres ? null : metadataWrappers(group);
        if (wrappers) {
            metadataRows(group, wrappers, before, after, dictionary);
        } else if (genres) {
            var rowsOk = group.querySelectorAll('.pbRow').length === before.length + after.length
                && (!before.length || sameRow(genres.previousElementSibling, before[0]));
            var next = studios.nextElementSibling;
            for (var i = 0; rowsOk && i < after.length; i++) {
                rowsOk = sameRow(next, after[i]);
                next = next && next.nextElementSibling;
            }
            if (!rowsOk) {
                var rows = group.querySelectorAll('.pbRow');
                for (var r = 0; r < rows.length; r++) {
                    rows[r].parentNode.removeChild(rows[r]);
                }
                if (before.length) {
                    genres.parentNode.insertBefore(row(before[0][0], before[0][1], before[0][2]), genres);
                }
                var last = studios;
                after.forEach(function (spec) {
                    var elem = row(spec[0], spec[1], spec[2]);
                    last.parentNode.insertBefore(elem, last.nextSibling);
                    last = elem;
                });
            }

            setText(view.querySelector('.directorsLabel'), translate(dictionary, view.querySelectorAll('.directors a').length > 1 ? 'Developers' : 'LabelDeveloper'));
            setText(view.querySelector('.studiosLabel'), translate(dictionary, view.querySelectorAll('.studios a').length > 1 ? 'Publishers' : 'Publisher'));
        }

        var misc = view.querySelector('.itemMiscInfo-secondary');
        var infos = [];
        display.secondaryInfo.forEach(function (part) {
            if (part === 'LastPlayed' && version.lastPlayed) {
                infos.push(translate(dictionary, 'LastPlayedValue').replace('{0}', formatDate(version.lastPlayed)));
            } else if (part === 'Added' && game.added) {
                infos.push(translate(dictionary, 'AddedOnValue').replace('{0}', formatDate(game.added)));
            }
        });
        var current = misc.querySelectorAll('.pbRow');
        var infosOk = current.length === infos.length;
        for (var n = 0; infosOk && n < infos.length; n++) {
            infosOk = current[n].textContent === infos[n];
        }
        if (!infosOk) {
            for (var c = 0; c < current.length; c++) {
                misc.removeChild(current[c]);
            }
            infos.forEach(function (text) {
                var info = document.createElement('div');
                info.className = 'mediaInfoItem pbRow';
                info.textContent = text;
                misc.appendChild(info);
            });
        }
        setHidden(misc, !misc.querySelector('.mediaInfoItem'));

        if (libraryId) {
            (genres ? [['genre', '.genres a'], ['developer', '.directors a'], ['publisher', '.studios a']]
                : [['genre', '.pbMetaGenre a'], ['developer', '.pbMetaDirector a'], ['publisher', '.pbMetaStudio a']]).concat([['tag', '.itemTags a']]).forEach(function (entry) {
                var links = view.querySelectorAll(entry[1]);
                for (var l = 0; l < links.length; l++) {
                    var href = gameHref(entry[0], links[l].getAttribute('href'), libraryId, route);
                    if (href && links[l].getAttribute('href') !== href) {
                        links[l].setAttribute('href', href);
                    }
                }
            });
        }
    }

    // Re-applies the game's state after each change Jellyfin makes to the page; only for the view's latest viewshow.
    function watch(view, request, redo) {
        if (view.pbObserver) {
            view.pbObserver.disconnect();
        }
        var observer = new MutationObserver(function (records) {
            if (!ghostOnly(records) && view.pbRequest === request && view.classList.contains('pbGame')) {
                redo();
            }
        });
        ['.itemDetailsGroup', '.detailImageContainer', '.itemMiscInfo-primary', '.itemMiscInfo-secondary', '.itemExternalLinks', '.itemTags', '.trackSelections', '.mainDetailButtons'].forEach(function (selector) {
            var target = view.querySelector(selector);
            if (target) {
                observer.observe(target, { childList: true, subtree: true, characterData: true, attributes: true, attributeOldValue: true, attributeFilter: ['href', 'class'] });
            }
        });
        view.pbObserver = observer;
    }

    // A version chosen in the list: that version's detail page replaces this one, so Jellyfin draws every field itself (replace: no history entry per
    // version). Jellyfin's own change handler of .selectSource needs media sources it never stored for a virtual item, so on game pages the event
    // stops here.
    function onVersionChange(e) {
        var view = this;
        if (!e.target.classList || !e.target.classList.contains('selectSource') || !view.classList.contains('pbGame')) {
            return;
        }
        e.stopPropagation();
        if (e.target.value) {
            pickVersion(view, e.target.value);
        }
    }

    // One version at a time: Jellyfin renders whatever answer arrives last, so a second pick during a switch waits (only the latest is kept) until
    // the first is done; a switch that never finishes stops blocking after 5 s.
    // Set when Jellyfin drew another item than the one asked for (a later 12.x that copies params): from then on a pick navigates. Kept in
    // sessionStorage so the reload that follows remembers it.
    var inPlaceBroken = false;
    try {
        inPlaceBroken = window.sessionStorage.getItem('pbInPlaceBroken') === '1';
    } catch (e) {
    }

    // False only when the cover of the detail page names another version of this game than id; no cover yet, no id or an id that is no version of the
    // game counts as unknown.
    function renderedFor(view, id, game) {
        var cover = view.querySelector('.detailImageContainer .cardImageContainer');
        var url = cover && (cover.getAttribute('data-src') || cover.style.backgroundImage);
        var m = url && /\/Items\/([0-9a-f]{32})\//i.exec(url);
        if (!m) {
            return true;
        }
        var shown = m[1].toLowerCase();
        var wanted = id.replace(/-/g, '').toLowerCase();
        return shown === wanted || !(game.versions || []).some(function (v) {
            return v && typeof v.id === 'string' && v.id.replace(/-/g, '').toLowerCase() === shown;
        });
    }

    function pickVersion(view, id) {
        // A view the user left (Back) takes no pick: it would rewrite the URL of the page the user is on now.
        if (view.pbLeft || view.classList.contains('hide')) {
            return;
        }
        if (view.pbSwitching && Date.now() - view.pbSwitchAt < 5000) {
            view.pbPending = id;
            return;
        }
        view.pbPending = null;
        if (id.replace(/-/g, '').toLowerCase() === view.pbVersion) {
            return;
        }
        var hash = '#/details?id=' + encodeURIComponent(id) + '&serverId='
            + encodeURIComponent(routeParam('details', 'serverId') || window.ApiClient.serverId());
        if (inPlaceBroken || !switchInPlace(view, id, hash, true)) {
            window.location.replace(hash);
        }
    }

    // A version picked on the standing page is drawn by Jellyfin, ArtworkPlus and us, which would show several painted states. A view transition
    // keeps the browser's picture of the old page on screen (pbHold rules in #pbStyles) and shows the result in one step once settled (settleHold).
    // Without view transitions the switch runs as before. null while no switch is held.
    var hold = null;

    function noop() {
    }

    // The version-switch machinery (hold release signal, poster overlay, prefetch) runs only for a game with more than one version.
    var HOLD_CAP_MS = 1000;      // the held picture goes at the latest after this (the animation clock of 1.5 s stays the last back-stop)
    var HOLD_ROWS_MS = 450;      // the tile rows hold the picture only this long (holdBusy)
    var COVER_WAIT_MS = 250;     // the hold waits for the new cover's picture (set, decoded) only this long; the overlay covers the rest
    var ROW_SELECTOR = '[id$="Collapsible"], .itemsContainer';
    var POSTER_FADE_MS = 600;    // the old cover fades out over the new one
    var PREFETCH_CAP = 8;        // versions asked and pictures loaded at most
    var pickGen = 0;
    // Jellyfin's own getItem (before our wrapper, set by wrapGetItem): the prefetch must not start the wrapper's games/{id} request.
    var getItemRaw = null;

    function groupSize(view) {
        var game = view && view.pbGame;
        return game && game.versions ? game.versions.length : 0;
    }

    function reducedMotion() {
        try {
            return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        } catch (e) {
            return false;
        }
    }

    function abortHold() {
        if (hold) {
            hold.aborted = true;
            hold.transition.skipTransition();
        }
    }

    // The held picture goes (page quiet, cap reached, or the browser ended the transition): the old cover's overlay is put in place first, then the
    // picture goes, the release signal for ArtworkPlus follows, then the old cover fades out.
    function releaseHold(current, byBrowser) {
        if (current.released) {
            return;
        }
        current.released = true;
        var view = current.view;
        if (byBrowser || current.aborted) {
            if (!byBrowser) {
                current.transition.skipTransition();
            }
            if (!current.aborted) {
                announceRelease(view, current.cover);
            }
            return;
        }
        var proceed = function () {
            if (current.aborted || (hold && hold !== current)) {
                return;
            }
            var ended = hold !== current;
            var state = releaseArmed(view) ? placeCover(view, current.cover) : null;
            var go = function () {
                current.transition.skipTransition();
                if (current.aborted || (hold && hold !== current)) {
                    return;
                }
                if (state && !releaseArmed(view)) {
                    removeGhost(true);
                }
                announceRelease(view, current.cover, state);
            };
            if (state && !ended) {
                requestAnimationFrame(go);
            } else {
                go();
            }
        };
        picturesReady(current.cover, 150).then(proceed).catch(function () {
            // proceed threw (released is already set): still let the held picture go and announce the release, once
            if (current.aborted || (hold && hold !== current)) {
                return;
            }
            try {
                current.transition.skipTransition();
            } catch (e) {
            }
            announceRelease(view, null);
        });
    }

    function releaseArmed(view) {
        return !!view.pbRelPending && view.pbShownGen === view.pbRelGen && groupSize(view) > 1;
    }

    // pb:switchrelease names the generation of the pick whose viewshow was dispatched last, so ArtworkPlus can tell an overtaken pick from the
    // current one; it waits until that viewshow has run. placed: the overlay releaseHold put in place.
    function announceRelease(view, cover, placed) {
        if (!view.pbRelPending || view.pbShownGen !== view.pbRelGen) {
            return;
        }
        view.pbRelPending = false;
        if (groupSize(view) < 2) {
            return;
        }
        document.dispatchEvent(new CustomEvent('pb:switchrelease', { detail: { id: view.pbVersion, first: view.pbFirst, generation: view.pbShownGen } }));
        if (cover) {
            startCoverFade(view, cover, placed);
        }
    }

    // ---- The old cover as an overlay ----
    // A child of the visible cover (absolute, z-index 0, pointer-events none), so the version marker stays above and the new cover under it. If
    // Jellyfin rebuilds .detailImageContainer later, the overlay moves to the new cover. Its mutations are ignored by the observers (ghostOnly).
    var ghostState = null;

    function pictureUrl(el) {
        var m = el && /url\(["']?([^"')]+)["']?\)/.exec(getComputedStyle(el).backgroundImage || '');
        return m ? m[1] : '';
    }

    function coverElement(view) {
        var all = view.querySelectorAll('.detailImageContainer .cardImageContainer');
        for (var i = 0; i < all.length; i++) {
            if (all[i].getClientRects().length) {
                return all[i];
            }
        }
        return all[0] || null;
    }

    function classesWithoutToggle(classes) {
        return classes.split(/\s+/).filter(function (name) {
            return name && name !== COVER_SHOWN;
        }).sort().join(' ');
    }

    // True when every change of the records came from the overlay or from the cover-shown class toggle.
    function ghostOnly(records) {
        return records.every(function (record) {
            var target = record.target.nodeType === 1 ? record.target : record.target.parentElement;
            if (target && target.closest('.pbGhost')) {
                return true;
            }
            if (record.type === 'attributes' && record.attributeName === 'class' && typeof record.oldValue === 'string' && target && typeof target.className === 'string') {
                return classesWithoutToggle(record.oldValue) === classesWithoutToggle(target.className);
            }
            var nodes = [].slice.call(record.addedNodes).concat([].slice.call(record.removedNodes));
            return record.type === 'childList' && nodes.length > 0 && nodes.every(function (node) {
                return node.classList && node.classList.contains('pbGhost');
            });
        });
    }

    // Two cover URLs show the same picture when they match without the item id (path, parameters and tag); a wrong "different" only costs an overlay
    // that fades a picture into itself.
    function samePicture(oldUrl, newUrl) {
        var strip = function (url) {
            return url.replace(/\/Items\/[0-9a-f-]{32,36}\//i, '/Items/*/');
        };
        return !!oldUrl && !!newUrl && strip(oldUrl) === strip(newUrl);
    }

    // What is on screen at the cover now, as layers bottom to top (a pick during a fade starts from the mix on screen, no jump). null: nothing to
    // fade (or reduced motion).
    function captureCover(view) {
        if (reducedMotion()) {
            return null;
        }
        var layers = [];
        var shade = ghostState && ghostState.layers.length ? parseFloat(getComputedStyle(ghostState.el).opacity) : 0;
        var el = coverElement(view);
        var url = pictureUrl(el);
        if (url && shade < 0.999) {
            var s = getComputedStyle(el);
            layers.push({ url: url, size: s.backgroundSize, position: s.backgroundPosition, repeat: s.backgroundRepeat, radius: s.borderRadius, opacity: 1 });
        }
        if (shade > 0) {
            var top = ghostState.layers[ghostState.layers.length - 1];
            layers.push(Object.assign({}, top, { opacity: shade * top.opacity }));
        }
        layers.forEach(preloadLayer);
        return layers.length ? { layers: layers } : null;
    }

    // The old cover's picture is read into memory when it is taken and the overlay paints that copy's object URL, so the release causes no request. A
    // failed read falls back to the URL.
    var blobCopies = [];

    function preloadLayer(layer) {
        if (layer.ready) {
            return;
        }
        var now = Date.now();
        blobCopies = blobCopies.filter(function (copy) {
            if (now - copy.at > 60000) {
                URL.revokeObjectURL(copy.url);
                return false;
            }
            return true;
        });
        var show = function (src) {
            var image = new Image();
            image.decoding = 'async';
            layer.img = image;
            image.src = src;
            return image.decode();
        };
        layer.ready = Promise.resolve(typeof fetch === 'function' ? fetch(layer.url, { cache: 'force-cache', credentials: 'same-origin' }) : null).then(function (response) {
            if (!response || !response.ok) {
                throw new Error('no copy');
            }
            return response.blob();
        }).then(function (blob) {
            var url = URL.createObjectURL(blob);
            blobCopies.push({ url: url, at: Date.now() });
            layer.copy = url;
            return show(url);
        }).catch(function () {
            return show(layer.url);
        }).then(noop, noop);
    }

    function picturesReady(cover, ms) {
        var waits = cover ? cover.layers.map(function (layer) {
            return layer.ready;
        }).filter(Boolean) : [];
        if (!waits.length) {
            return Promise.resolve();
        }
        return Promise.race([Promise.all(waits), new Promise(function (resolve) {
            setTimeout(resolve, ms);
        })]);
    }

    function revokeCopies(layers) {
        blobCopies = blobCopies.filter(function (copy) {
            if (!layers || layers.some(function (layer) {
                return layer.copy === copy.url;
            })) {
                URL.revokeObjectURL(copy.url);
                return false;
            }
            return true;
        });
    }

    function removeGhost(done) {
        if (ghostState) {
            var state = ghostState;
            ghostState = null;
            if (done) {
                revokeCopies(state.layers);
            }
            if (state.animation) {
                state.animation.cancel();
            }
            if (state.el.parentNode) {
                state.el.parentNode.removeChild(state.el);
            }
        }
    }

    function placeCover(view, cover) {
        var box = coverElement(view);
        if (!box || !cover || !cover.layers.length || !box.getClientRects().length) {
            return null;
        }
        removeGhost();
        var fill = 'position:absolute;left:0;top:0;right:0;bottom:0;pointer-events:none;';
        var el = document.createElement('div');
        el.className = 'pbGhost';
        el.style.cssText = fill + 'z-index:0;border-radius:' + cover.layers[0].radius;
        cover.layers.forEach(function (layer) {
            var part = document.createElement('div');
            part.style.cssText = fill + 'background-image:url("' + (layer.copy || layer.url).replace(/"/g, '%22') + '");background-size:' + layer.size + ';background-position:'
                + layer.position + ';background-repeat:' + layer.repeat + ';border-radius:' + layer.radius + ';opacity:' + layer.opacity;
            el.appendChild(part);
        });
        box.appendChild(el);
        var state = ghostState = { el: el, animation: null, layers: cover.layers };
        keepCoverShown(box);
        return state;
    }

    // The new cover must be visible under the overlay: with ArtworkPlus it may sit at opacity 0 for a while. A class of ours (important opacity 1)
    // keeps it visible while the overlay fades out and until the cover would be visible by itself.
    var coverKeep = null;
    var COVER_SHOWN = 'pbCoverShown';

    function releaseCoverShown() {
        if (coverKeep) {
            var keep = coverKeep;
            coverKeep = null;
            clearTimeout(keep.timer);
            keep.box.classList.remove(COVER_SHOWN);
        }
    }

    function keepCoverShown(box) {
        releaseCoverShown();
        if (parseFloat(getComputedStyle(box).opacity) >= 0.99) {
            return;
        }
        box.classList.add(COVER_SHOWN);
        var keep = coverKeep = { box: box, timer: 0 };
        var began = Date.now();
        var tick = function () {
            if (coverKeep !== keep) {
                return;
            }
            if (box.isConnected === false || Date.now() - began > 5000) {
                releaseCoverShown();
                return;
            }
            if (!ghostState) {
                box.classList.remove(COVER_SHOWN);
                if (parseFloat(getComputedStyle(box).opacity) >= 0.99) {
                    coverKeep = null;
                    return;
                }
                box.classList.add(COVER_SHOWN);
            }
            keep.timer = setTimeout(tick, 100);
        };
        keep.timer = setTimeout(tick, 100);
    }

    function startCoverFade(view, cover, placed) {
        var state = placed || placeCover(view, cover);
        if (!state) {
            return;
        }
        var el = state.el;
        var began = Date.now();
        // The overlay stays on the old picture until the new cover is decoded, else the fade would show the placeholder.
        var wait = function () {
            if (ghostState !== state) {
                return;
            }
            var now = coverElement(view);
            if (now && el.parentNode !== now) {
                now.appendChild(el);
            }
            var url = pictureUrl(now);
            var same = !!url && cover.layers.every(function (layer) {
                return samePicture(layer.url, url);
            });
            if ((!now || !url || same) && Date.now() - began < 700) {
                requestAnimationFrame(wait);
                return;
            }
            if (!now || same) {
                removeGhost(true);
                return;
            }
            var go = function () {
                if (ghostState !== state) {
                    return;
                }
                var here = coverElement(view);
                if (here && el.parentNode !== here) {
                    here.appendChild(el);
                }
                state.animation = el.animate([{ opacity: 1 }, { opacity: 0 }], { duration: POSTER_FADE_MS, easing: 'ease', fill: 'forwards' });
                state.animation.onfinish = function () {
                    if (ghostState === state) {
                        removeGhost(true);
                    }
                };
            };
            if (!url) {
                go();
                return;
            }
            var image = new Image();
            image.src = url;
            var done = false;
            var once = function () {
                if (!done) {
                    done = true;
                    go();
                }
            };
            image.decode().then(once, once);
            setTimeout(once, 400);
        };
        requestAnimationFrame(wait);
    }

    // ---- Prefetch of the other versions ----
    // Once the page is complete and the browser idle: the other versions' answers (getItem through our wrapper) and their cover and logo, at low
    // priority, PREFETCH_CAP pictures at most, then pb:prefetch for ArtworkPlus. Released on leaving the page.
    function otherVersions(view) {
        var game = view.pbGame;
        if (groupSize(view) < 2) {
            return [];
        }
        return game.versions.map(function (v) {
            return v && typeof v.id === 'string' ? v.id.replace(/-/g, '').toLowerCase() : '';
        }).filter(function (id) {
            return id && id !== view.pbVersion;
        }).slice(0, PREFETCH_CAP);
    }

    // What was prefetched for the view's game, per version set (key): touched versions, answers (items), picture URLs asked (urls) and their Image
    // objects (imgs). Nothing is requested twice.
    function prefetchMemo(view) {
        var key = view.pbGame.versions.map(function (v) {
            return v && v.id;
        }).join('|');
        var memo = view.pbPrefetchMemo;
        if (!memo || memo.key !== key) {
            releaseMemo(view);
            memo = view.pbPrefetchMemo = { key: key, touched: {}, items: {}, urls: {}, imgs: [], done: false };
        }
        return memo;
    }

    function releaseMemo(view) {
        var memo = view.pbPrefetchMemo;
        if (memo) {
            view.pbPrefetchMemo = null;
            memo.imgs.forEach(function (image) {
                image.src = '';
            });
            memo.imgs = [];
        }
    }

    // A pick stops the run: what is not finished is cancelled (and asked again later), what is finished stays. leaving: the visit is over
    // (a leave, a different game), everything is released.
    function abortPrefetch(view, leaving) {
        var run = view && view.pbPrefetch;
        if (run) {
            view.pbPrefetch = null;
            run.aborted = true;
            clearTimeout(run.timer);
            var memo = view.pbPrefetchMemo;
            if (memo) {
                memo.imgs = memo.imgs.filter(function (image) {
                    if (image.complete === true) {
                        return true;
                    }
                    image.src = '';
                    delete memo.urls[image.pbUrl];
                    return false;
                });
            }
        }
        if (leaving) {
            releaseMemo(view);
        }
    }

    function whenIdle(fn) {
        (window.requestIdleCallback || function (f) {
            return setTimeout(f, 0);
        })(fn, { timeout: 1000 });
    }

    function versionUrl(template, vid, tag) {
        if (!template || !tag || !/[?&]tag=/.test(template) || !/\/Items\/[0-9a-f-]{32,36}\//i.test(template)) {
            return '';
        }
        return template.replace(/\/Items\/[0-9a-f-]{32,36}\//i, '/Items/' + vid + '/').replace(/([?&]tag=)[^&]*/, function (all, head) {
            return head + encodeURIComponent(tag);
        });
    }

    function schedulePrefetch(view) {
        if (groupSize(view) < 2 || view.pbLeft || !window.ApiClient) {
            return;
        }
        var memo = prefetchMemo(view);
        if (memo.done || (view.pbPrefetch && view.pbPrefetch.key === memo.key)) {
            return;
        }
        abortPrefetch(view);
        var run = view.pbPrefetch = { key: memo.key, aborted: false, timer: 0, tries: 0 };
        var attempt = function () {
            if (run.aborted || view.pbPrefetch !== run) {
                return;
            }
            // not while a switch is drawn or held, nor on a left page; a few tries, then it stays undone for this visit
            if (view.pbLeft || view.pbSwitching || hold || document.hidden) {
                if (++run.tries < 12) {
                    run.timer = setTimeout(attempt, 600);
                }
                return;
            }
            whenIdle(function () {
                if (!run.aborted && view.pbPrefetch === run) {
                    startPrefetch(view, run);
                }
            });
        };
        run.timer = setTimeout(attempt, 800);
    }

    function pool(list, limit, run, worker) {
        return new Promise(function (resolve) {
            var next = 0;
            var active = 0;
            var pump = function () {
                while (!run.aborted && active < limit && next < list.length) {
                    active++;
                    Promise.resolve(list[next++]).then(worker).catch(noop).then(done);
                }
                if (!active) {
                    resolve();
                }
            };
            var done = function () {
                active--;
                pump();
            };
            pump();
        });
    }

    // The answers one at a time, each started when the browser is idle and only while the run lives, through Jellyfin's own getItem (getItemRaw).
    // Pictures: two at a time, fetch priority low. A pick aborts the run; the switch's finish schedules it again.
    function startPrefetch(view, run) {
        var api = window.ApiClient;
        if (!view.pbGame || !api.getItem || !api.getCurrentUserId) {
            return;
        }
        var memo = prefetchMemo(view);
        memo.touched[view.pbVersion] = true;
        var others = otherVersions(view);
        var ids = others.filter(function (id) {
            return !memo.touched[id] && !(id in memo.items);
        });
        if (!others.length) {
            return;
        }
        var fetchItem = function (userId, id) {
            return getItemRaw ? getItemRaw.call(api, userId, id) : api.getItem(userId, id);
        };
        var templates = { Primary: pictureUrl(coverElement(view)), Logo: pictureUrl(view.querySelector('.detailLogo')) };
        var config = null;
        clientConfiguration(api).catch(function () {
            return null;
        }).then(function (answer) {
            config = answer;
            return pool(ids, 1, run, function (vid) {
                return new Promise(whenIdle).then(function () {
                    if (run.aborted) {
                        return;
                    }
                    return Promise.resolve(fetchItem(api.getCurrentUserId(), vid)).catch(function () {
                        return null;
                    }).then(function (item) {
                        if (view.pbPrefetchMemo === memo) {
                            memo.items[vid] = item || false;
                        }
                    });
                });
            });
        }).then(function () {
            if (run.aborted || view.pbPrefetch !== run) {
                return;
            }
            var rules = config && config.VersionArtwork;
            var urls = [];
            others.forEach(function (vid) {
                var item = memo.items[vid];
                if (!item || !item.ImageTags) {
                    return;
                }
                var shown = Object.assign({}, item);
                if (rules && view.pbFirstImages && vid !== view.pbFirst && view.pbFirstImages.id === view.pbFirst) {
                    applyFirst(shown, imagesOf(shown, vid), view.pbFirstImages, rules, config);
                }
                ['Primary', 'Logo'].forEach(function (type) {
                    var url = versionUrl(templates[type], vid, shown.ImageTags && shown.ImageTags[type]);
                    if (url && urls.indexOf(url) === -1 && !memo.urls[url]) {
                        urls.push(url);
                    }
                });
            });
            return pool(urls.slice(0, Math.max(0, PREFETCH_CAP - Object.keys(memo.urls).length)), 2, run, function (url) {
                if (run.aborted) {
                    return;
                }
                var image = new Image();
                image.decoding = 'async';
                image.fetchPriority = 'low';
                image.pbUrl = url;
                image.src = url;
                memo.urls[url] = true;
                memo.imgs.push(image);
                return Promise.race([image.decode().catch(noop), new Promise(function (resolve) {
                    setTimeout(resolve, 3000);
                })]);
            });
        }).then(function () {
            if (!run.aborted && view.pbPrefetch === run && view.pbVersion && !memo.done) {
                memo.done = true;
                document.dispatchEvent(new CustomEvent('pb:prefetch', { detail: { current: view.pbVersion, versions: otherVersions(view) } }));
            }
        }, noop);
    }

    function clearHold(current) {
        if (hold === current) {
            hold = null;
        }
        clearTimeout(current.cap);
        current.observer.disconnect();
        document.documentElement.classList.remove('pbHold');
    }

    function holdSwitch(view, update) {
        if (hold && hold.released) {
            // A pick between the release and the end of the transition starts a hold of its own and gets its own release signal.
            clearHold(hold);
        }
        if (hold) {
            // A pick while a switch is held: drawn under the same picture, which waits for this switch's finishSwitch; the cap still counts from the
            // first picture.
            if (hold.view === view) {
                hold.round++;
                hold.settling = false;
                hold.quietAt = hold.roundAt = Date.now();
            }
            update();
            return;
        }
        if (typeof document.startViewTransition !== 'function') {
            if (groupSize(view) > 1 && !view.pbCover) {
                view.pbCover = captureCover(view);
            }
            removeGhost();
            update();
            return;
        }
        // The poster as it stands (an overlay still fading included) is read in the callback, which runs after the browser took its picture and
        // before the page is replaced.
        var current;
        // An overlay still fading is stopped at the mix it shows now, so the held picture and the overlay agree.
        if (ghostState && ghostState.animation) {
            ghostState.animation.pause();
        }
        var run = function () {
            if (current && groupSize(view) > 1) {
                current.cover = captureCover(view);
            }
            removeGhost();
            update();
        };
        var transition;
        try {
            transition = document.startViewTransition(run);
        } catch (e) {
            run();
            return;
        }
        current = hold = { view: view, round: 0, quietAt: Date.now(), roundAt: Date.now(), startAt: Date.now(), transition: transition, cover: null };
        current.observer = new MutationObserver(function (records) {
            if (!ghostOnly(records)) {
                current.quietAt = Date.now();
                if (!current.warming) {
                    current.warming = true;
                    requestAnimationFrame(function () {
                        current.warming = false;
                        warmCover(current);
                    });
                }
            }
        });
        current.observer.observe(view, { childList: true, subtree: true, attributes: true, attributeOldValue: true, characterData: true });
        current.observer.observe(document.body, { childList: true });
        document.documentElement.classList.add('pbHold');
        // The cap without the animation clock (a throttled or paused animation would keep the picture longer).
        current.cap = setTimeout(function () {
            releaseHold(current);
        }, HOLD_CAP_MS);
        var watch = function () {
            if (hold !== current || current.released) {
                return;
            }
            if (Date.now() - current.startAt >= HOLD_CAP_MS) {
                releaseHold(current);
                return;
            }
            requestAnimationFrame(watch);
        };
        requestAnimationFrame(watch);
        var end = function () {
            if (hold === current) {
                clearHold(current);
                releaseHold(current, true);
            }
        };
        current.transition.ready.catch(noop);
        current.transition.updateCallbackDone.catch(noop);
        current.transition.finished.then(end, end);
    }

    // True while the held page still changes on screen: an image not loaded or faded in yet (counted for 600 ms after the latest switch began), or a
    // fade or transition running on the page or on ArtworkPlus's art.
    function isCover(node) {
        return !!(node && node.closest && node.closest('.detailImageContainer .cardImageContainer'));
    }

    // The cover is busy while Jellyfin has not set its picture or the picture is not decoded yet; both count for COVER_WAIT_MS after the latest
    // switch began. A cover still decoding at the release is fine, since the overlay stays on the old picture until then.
    function decodeOnce(current, url) {
        var pics = current.pics = current.pics || {};
        var entry = pics[url];
        if (!entry) {
            entry = pics[url] = { ready: false };
            var image = new Image();
            image.decoding = 'async';
            image.src = url;
            Promise.resolve(image.decode()).then(function () {
                entry.ready = true;
            }, function () {
                entry.ready = true;
            });
        }
        return entry;
    }

    function warmCover(current) {
        if (hold !== current || current.released) {
            return;
        }
        var node = coverElement(current.view);
        if (node && !node.hasAttribute('data-src') && !node.classList.contains('lazy-hidden')) {
            var url = pictureUrl(node);
            if (url) {
                decodeOnce(current, url);
            }
        }
    }

    function coverBusy(current, node, age) {
        if (node.hasAttribute('data-src') || node.classList.contains('lazy-hidden')) {
            return age < COVER_WAIT_MS;
        }
        var url = pictureUrl(node);
        if (!url) {
            return false;
        }
        return !decodeOnce(current, url).ready && age < COVER_WAIT_MS;
    }

    function holdBusy(current) {
        var view = current.view;
        var age = Date.now() - current.roundAt;
        var loading = age < 600;
        // The tile rows below the header hold the picture only for the first 450 ms, so a late card image does not keep it on screen.
        var lateRows = age >= HOLD_ROWS_MS;
        var waiting = view.querySelectorAll('.lazy[data-src], .lazy-hidden:not(canvas), .lazy-image-fadein-fast, .lazy-image-fadein');
        for (var i = 0; i < waiting.length; i++) {
            if (isCover(waiting[i])) {
                // The cover is quiet when its picture is set and decoded, not when it is visible (ArtworkPlus may keep it at opacity 0 for a while).
                if (coverBusy(current, waiting[i], age)) {
                    return true;
                }
                continue;
            }
            var unloaded = waiting[i].hasAttribute('data-src') || waiting[i].classList.contains('lazy-hidden');
            if (unloaded ? !loading : getComputedStyle(waiting[i]).opacity === '1') {
                continue;
            }
            if (lateRows && waiting[i].closest(ROW_SELECTOR)) {
                continue;
            }
            var rect = waiting[i].getBoundingClientRect();
            if (rect.width && rect.height && rect.bottom > 0 && rect.top < window.innerHeight && rect.right > 0 && rect.left < window.innerWidth) {
                return true;
            }
        }
        return document.getAnimations().some(function (animation) {
            var target = animation.effect && animation.effect.target;
            return animation.playState === 'running' && target && !animation.effect.pseudoElement && animation.effect.getTiming().iterations !== Infinity
                && !target.closest('.pbGhost') && !isCover(target) && !(lateRows && target.closest(ROW_SELECTOR))
                && (view.contains(target) || !!target.closest('.characterart-container'));
        });
    }

    // The switch is drawn: the held picture goes once the page has been quiet for 100 ms. A later pick under the same hold ends this check.
    // Rows a version does not have keep their height during a switch and give it back when it has settled.
    function keepRoom(view, wrap) {
        (view.pbRoom = view.pbRoom || []).push(wrap);
    }

    function releaseRoom(view) {
        (view.pbRoom || []).forEach(function (wrap) {
            if (!wrap.firstChild) {
                wrap.style.minHeight = '';
            }
        });
        view.pbRoom = [];
    }

    function settleHold(view) {
        if (view) {
            view.classList.remove('pbKeepSimilar');
            releaseRoom(view);
        }

        var current = hold;
        if (!current || current.released) {
            if (view && view.pbRelPending) {
                var cover = view.pbCover;
                view.pbCover = null;
                announceRelease(view, cover);
            }
            return;
        }
        if (current.settling) {
            return;
        }
        current.settling = true;
        var round = current.round;
        var check = function () {
            if (hold !== current || current.round !== round) {
                return;
            }
            if (Date.now() - current.quietAt >= 100 && !holdBusy(current)) {
                releaseHold(current);
                return;
            }
            requestAnimationFrame(check);
        };
        requestAnimationFrame(check);
    }

    // The switch is drawn (or has no game answer): the waiting pick, if any and not the version now shown, goes on.
    // A held picture stays while a waiting pick follows (it is drawn under the same hold), else it settles now.
    function finishSwitch(view) {
        view.pbSwitching = false;
        var next = view.pbPending;
        view.pbPending = null;
        if (!next || next.replace(/-/g, '').toLowerCase() === view.pbVersion) {
            settleHold(view);
            schedulePrefetch(view);
        } else {
            // Jellyfin draws the rows of the item after the name; a second render starting inside them leaves stray scroll buttons, so the waiting
            // pick starts after a short quiet time.
            view.pbPending = next;
            view.pbSwitching = true;
            view.pbSwitchAt = Date.now();
            clearTimeout(view.pbQuiet);
            view.pbQuiet = setTimeout(function () {
                var again = view.pbPending;
                view.pbPending = null;
                view.pbSwitching = false;
                if (again && again.replace(/-/g, '').toLowerCase() !== view.pbVersion) {
                    pickVersion(view, again);
                } else {
                    settleHold(view);
                    schedulePrefetch(view);
                }
            }, 450);
        }
    }

    // The version shown on the standing view: no navigation, so no new view. Jellyfin's details controller reads params.id on every non-restored
    // viewshow, so the id is replaced there, the URL follows without a router event, and the view gets the viewbeforehide/viewshow pair it would get
    // between two visits. False: no params object (the caller navigates). held: a pick drawn under holdSwitch; a page left before the transition's
    // callback keeps its id and URL.
    function switchInPlace(view, id, hash, held) {
        var params = view.pbParams;
        if (!params || typeof params.id !== 'string' || !view.isConnected) {
            return false;
        }
        var from = window.location.hash;
        view.pbOverlap = !!view.pbSwitching;
        view.pbSwitching = true;
        var similarRow = view.querySelector('#similarCollapsible');
        view.classList.toggle('pbKeepSimilar', !!similarRow && !similarRow.classList.contains('hide'));
        var at = view.pbSwitchAt = Date.now();
        // Every pick counts (pbGeneration of its viewshow). A running prefetch stops, as the request goes first.
        var gen = ++pickGen;
        if (groupSize(view) > 1) {
            prefetchMemo(view).touched[view.pbVersion] = true;
        }
        abortPrefetch(view);
        if (held && groupSize(view) > 1) {
            view.pbRelPending = true;
            view.pbRelGen = gen;
        }
        setTimeout(function () {
            if (view.pbSwitching && view.pbSwitchAt === at) {
                finishSwitch(view);
            }
        }, 5100);
        var show = function () {
            if (held && (view.pbLeft || window.location.hash !== from)) {
                if (view.pbSwitchAt === at) {
                    view.pbSwitching = false;
                    view.pbPending = null;
                    view.pbOverlap = false;
                }
                view.pbRelPending = false;
                view.pbCover = null;
                if (hold && hold.view === view) {
                    abortHold();
                }
                return;
            }
            view.pbInPlace = true;
            view.pbArtKeep = true;
            view.pbShownGen = gen;
            params.id = id;
            // The history entry keeps the version the page was opened with (Artwork on Version Change): Back to this entry builds a new view
            // (Jellyfin caches views by the URL they were made for), which takes it over (pbCarried in the viewshow handler).
            window.history.replaceState(firstState(view.pbFirst, id.replace(/-/g, '').toLowerCase()), '', hash);
            view.dispatchEvent(new CustomEvent('viewbeforehide', { bubbles: true, detail: { type: view.getAttribute('data-type'), params: params, isRestored: false, options: {}, pbInPlace: true, pbGeneration: gen } }));
            view.dispatchEvent(new CustomEvent('viewshow', { bubbles: true, detail: { type: view.getAttribute('data-type'), params: params, isRestored: false, options: {}, pbInPlace: true, pbGeneration: gen } }));
        };
        if (held) {
            holdSwitch(view, show);
        } else {
            show();
        }
        return true;
    }

    // Artwork on Version Change: the history entry of a game page names the version the page was opened with (pbFirst) and the item that applies to
    // (pbFor), beside the router's own fields. Read back when a new view is built on the entry, and by ArtworkPlus.
    function firstState(first, forId) {
        var state = window.history.state;
        return (state === null || typeof state === 'object') && first ? Object.assign({}, state, { pbFirst: first, pbFor: forId }) : state;
    }

    function writeFirst(first, forId) {
        var state = window.history.state;
        if (state && typeof state === 'object' && state.pbFirst === first && state.pbFor === forId) {
            return;
        }
        var next = firstState(first, forId);
        if (next !== state) {
            window.history.replaceState(next, '');
        }
    }

    document.addEventListener('viewbeforeshow', function (e) {
        var view = e.target;
        if (view && view.id === 'itemDetailPage' && e.detail && !e.detail.isRestored && e.detail.params) {
            view.pbParams = e.detail.params;
        }
    }, true);

    // The tile rows special features, collections (ours) and "More like this" in the order of RowOrder. The DOM order changes, not a CSS order, so TV
    // focus follows; Jellyfin's two rows move only when RowOrder puts "More like this" first. 12.1 has its own Collections row; where the page has
    // it, ours is never built (feature test, no version number).
    var tileRows = { Specials: 'specialsCollapsible', Screenshots: 'pbScreenshotsCollapsible', Wallpapers: 'pbWallpapersCollapsible', Documents: 'pbDocumentsCollapsible', Steamspecials: 'pbSteamSpecialsCollapsible', Videos: 'pbVideoRowsCollapsible', Vrowsbefore: 'pbVideoRowsBefore', Collections: 'pbCollectionsCollapsible', Soundtrack: 'pbSoundtrackCollapsible', Related: 'pbRelatedCollapsible', Actors: 'pbActorsCollapsible', Connections: 'pbConnectionsCollapsible', Similar: 'similarCollapsible' };

    function tileRow(view, key) {
        return key === 'Collections' && view.querySelector('#collectionsCollapsible') || view.querySelector('#' + tileRows[key]);
    }

    // A built row moved into `parent` before `before` (null: at the end). Moving an emby-scroller makes the webcomponents polyfill detach and attach
    // it again, so it builds a new emby-scrollbuttons in front of itself and the old buttons would pile up. So the row is watched after the move:
    // every bar whose nextSibling is not the row's scroller is hidden at once and removed two frames later (an earlier removal throws in its
    // requestAnimationFrame). One observer per row, released 2 s after the move.
    function moveRow(row, parent, before) {
        if (!row.pbBarObserver) {
            row.pbBarObserver = new MutationObserver(function () {
                var scroller = row.querySelector('[is="emby-scroller"]');
                var bars = row.querySelectorAll('.emby-scrollbuttons');
                for (var i = 0; i < bars.length; i++) {
                    if (bars[i].nextSibling !== scroller && !bars[i].pbLeaving) {
                        leaveBar(bars[i]);
                    }
                }
            });
            row.pbBarObserver.observe(row, { childList: true });
        }
        parent.insertBefore(row, before);
        // The late bar arrives within moments; the observer is released after 2 s, not kept for the life of the row.
        clearTimeout(row.pbBarTimer);
        row.pbBarTimer = setTimeout(function () {
            if (row.pbBarObserver) {
                row.pbBarObserver.disconnect();
                row.pbBarObserver = null;
            }
        }, 2000);
    }

    function leaveBar(bar) {
        bar.pbLeaving = true;
        bar.style.display = 'none';
        requestAnimationFrame(function () {
            requestAnimationFrame(function () {
                if (bar.parentNode) {
                    bar.parentNode.removeChild(bar);
                }
            });
        });
    }

    function orderRows(view, order) {
        var native = !!view.querySelector('#collectionsCollapsible');
        var rows = (order.match(native ? /Specials|Collections|Similar/g : /Specials|Similar/g) || []).map(function (key) {
            return tileRow(view, key);
        }).filter(function (elem) {
            return !!elem;
        });
        var after = function (a, b) {
            return !!(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);
        };
        if (!native) {
            if (rows.length === 2 && !after(rows[0], rows[1])) {
                moveRow(rows[0], rows[1].parentNode, rows[1]);
            }
            return;
        }
        if (rows.every(function (elem, i) {
            return !i || after(rows[i - 1], elem);
        })) {
            return;
        }
        var first = rows.filter(function (elem) {
            return rows.every(function (other) {
                return other === elem || after(elem, other);
            });
        })[0];
        var parent = first.parentNode;
        var place = first.previousElementSibling;
        rows.forEach(function (elem) {
            var at = place ? place.nextElementSibling : parent.firstElementChild;
            if (at !== elem) {
                moveRow(elem, parent, at);
            }
            place = elem;
        });
    }

    // CSS `order` beats the DOM order inside a flex container (custom CSS may give Jellyfin's rows an order, while ours have 0).
    // The inline orders for the managed rows, taken from the orders their CSS gives them; null (leave untouched) while every CSS order is 0.
    function flexOrderValues(cssOrders) {
        var nonZero = cssOrders.filter(function (value) { return value !== 0; });
        if (!nonZero.length) {
            return null;
        }
        var base = Math.min.apply(null, nonZero);
        return cssOrders.map(function (value, i) { return base + i; });
    }

    function managedRows(view) {
        return ['Specials', 'Screenshots', 'Wallpapers', 'Documents', 'Steamspecials', 'Vrowsbefore', 'Videos', 'Soundtrack', 'Collections', 'Related', 'Actors', 'Connections', 'Similar'].map(function (key) {
            return tileRow(view, key);
        }).filter(function (elem) {
            return !!elem;
        });
    }

    function clearFlexOrder(view) {
        managedRows(view).forEach(function (elem) {
            if (elem.pbFlexOrder) {
                elem.style.order = '';
                elem.pbFlexOrder = false;
            }
        });
    }

    function orderFlex(view, order) {
        clearFlexOrder(view);
        if (view.querySelector('#collectionsCollapsible')) {
            return;
        }
        var rows = (order.match(/Screenshots|Wallpapers|Documents|Steamspecials|Vrowsbefore|Specials|Videos|Soundtrack|Collections|Related|Actors|Connections|Similar/g) || []).map(function (key) {
            return tileRow(view, key);
        }).filter(function (elem) {
            return !!elem;
        });
        if (rows.length < 2) {
            return;
        }
        var parent = rows[0].parentNode;
        var display = parent && window.getComputedStyle(parent).display;
        if (display !== 'flex' && display !== 'inline-flex' || rows.some(function (elem) { return elem.parentNode !== parent; })) {
            return;
        }
        var values = flexOrderValues(rows.map(function (elem) {
            return parseInt(window.getComputedStyle(elem).order, 10) || 0;
        }));
        if (values) {
            rows.forEach(function (elem, i) {
                elem.style.order = String(values[i]);
                elem.pbFlexOrder = true;
            });
        }
    }

    // Jellyfin's place of the "More like this" row (after scenes) for a view that now shows another item; on 12.1 after special features, scenes and
    // collections.
    function restoreRows(view) {
        clearFlexOrder(view);
        var scenes = view.querySelector('#scenesCollapsible');
        var similar = view.querySelector('#similarCollapsible');
        var collections = view.querySelector('#collectionsCollapsible');
        if (collections) {
            var chain = ['specialsCollapsible', 'musicVideosCollapsible', 'scenesCollapsible', 'collectionsCollapsible', 'similarCollapsible'].map(function (rowId) {
                return view.querySelector('#' + rowId);
            }).filter(function (elem) {
                return !!elem;
            });
            for (var i = 1; i < chain.length; i++) {
                if (!(chain[i - 1].compareDocumentPosition(chain[i]) & Node.DOCUMENT_POSITION_FOLLOWING)) {
                    moveRow(chain[i], chain[i - 1].parentNode, chain[i - 1].nextSibling);
                }
            }
            return;
        }
        if (similar && scenes && scenes.nextElementSibling !== similar) {
            moveRow(similar, scenes.parentNode, scenes.nextSibling);
        }
    }

    // Collections as Jellyfin 12.1 shows them on a detail page: a row of tiles placed by RowOrder, with the title Jellyfin uses for "Collections".
    // Own id, so a later 12.x port does not meet Jellyfin's.
    function setCollections(view, items, title, order) {
        var similar = view.querySelector('#similarCollapsible');
        var signature = items.map(function (item) {
            return item.Id;
        }).join(',');
        var existing = view.querySelector('#pbCollectionsCollapsible');
        if (existing) {
            existing.querySelector('.sectionTitle').textContent = title;
            if (existing.pbSignature !== signature) {
                existing.pbSignature = signature;
                existing.querySelector('.itemsContainer').innerHTML = collectionCards(items);
                fillImages(existing);
            }
            return;
        }
        if (!similar) {
            return;
        }
        var holder = document.createElement('div');
        holder.innerHTML = '<div id="pbCollectionsCollapsible" class="verticalSection detailVerticalSection">'
            + '<h2 class="sectionTitle sectionTitle-cards padded-right">' + escapeHtml(title) + '</h2>'
            + '<div is="emby-scroller" class="padded-top-focusscale padded-bottom-focusscale no-padding" data-centerfocus="true">'
            + '<div is="emby-itemscontainer" class="scrollSlider focuscontainer-x itemsContainer"></div></div></div>';
        var section = holder.firstChild;
        section.pbSignature = signature;
        orderRows(view, order);
        var keys = order.match(/Screenshots|Wallpapers|Documents|Steamspecials|Vrowsbefore|Specials|Videos|Soundtrack|Collections|Related|Actors|Connections|Similar/g) || [];
        var at = keys.indexOf('Collections');
        var present = function (key) {
            return !!tileRow(view, key);
        };
        var next = keys.slice(at + 1).filter(present)[0];
        var prev = keys.slice(0, at).filter(present).pop();
        if (next) {
            similar.parentNode.insertBefore(section, tileRow(view, next));
        } else {
            similar.parentNode.insertBefore(section, tileRow(view, prev).nextSibling);
        }
        orderFlex(view, order);
        // The tiles come in the next task, as Jellyfin fills its rows after the scroller is built; the grown row makes the scroller send the scroll
        // event that sets the buttons.
        setTimeout(function () {
            section.querySelector('.itemsContainer').innerHTML = collectionCards(items);
            fillImages(section);
        });
    }

    // Steam Special Features: the videos of the game's Steam store page as virtual Video items (games/{id}/steamvideos) in a row of tiles after the
    // special features. Jellyfin's items container starts playback and each item is kept in extraIds with this game. Built once; a standing view
    // keeps its row.
    // The label under a Steam video: Jellyfin's own word where it has one (Clip, Trailer, Preview, Event, Features), a trailer with a qualifier built
    // by SteamLabelTrailerOf, else our own word.
    var steamDictionary = {};
    var steamJellyfinWords = { Clip: 'Clip', Trailer: 'Trailer', Preview: 'Preview', Event: 'LabelEvent', Features: 'Features' };
    function steamLabelText(key) {
        var word = steamJellyfinWords[key];
        if (word) {
            return translate(steamDictionary, word) || key;
        }
        if (key && key.length > 7 && key.slice(-7) === 'Trailer') {
            var whole = translate(steamDictionary, 'SteamLabel' + key);
            if (whole) {
                return whole;
            }
            var qualifier = translate(steamDictionary, 'SteamLabel' + key.slice(0, -7)) || key.slice(0, -7);
            var template = translate(steamDictionary, 'SteamLabelTrailerOf') || '{0} {1}';
            if (/\{1\}\s+[^\s{}–-]+\s+\{0\}/.test(template)) {
                qualifier = qualifier.charAt(0).toLowerCase() + qualifier.slice(1);
            }
            return template.replace('{0}', qualifier).replace('{1}', translate(steamDictionary, 'Trailer') || 'Trailer');
        }
        return translate(steamDictionary, 'SteamLabel' + key) || key || '';
    }

    function steamLabelLink(text) {
        return "<bdi><a class='itemAction textActionButton' data-action='play' title=\"" + escapeHtml(text) + "\">" + escapeHtml(text) + '</a></bdi>';
    }

    var EXTRA_TYPE_WORDS = { Clip: ['Clip', 'Clip'], BehindTheScenes: ['BehindTheScenes', 'Behind the Scenes'], DeletedScene: ['DeletedScene', 'Deleted Scene'], Interview: ['Interview', 'Interview'],
        Scene: ['Scene', 'Scene'], Sample: ['Sample', 'Sample'], Short: ['Short', 'Short'], Featurette: ['Featurette', 'Featurette'], Trailer: ['Trailer', 'Trailer'] };
    function extraTypeLine(item, dictionary) {
        var word = EXTRA_TYPE_WORDS[item.ExtraType];
        return word ? (translate(dictionary || {}, word[0]) || word[1]) : '';
    }

    // shape: the tile shape of the row (a Steam row is always landscape); local: cards of local videos, whose image is cut to the tile when its ratio
    // is within 20 % of 16:9.
    function steamCards(items, shape, local, dictionary) {
        var classesOf = TILE_SHAPES[tileShape(shape)];
        var layout = document.documentElement.classList;
        var tv = layout.contains('layout-tv');
        var api = window.ApiClient;
        return items.map(function (item, index) {
            var name = escapeHtml(item.Name || '');
            var url = '#/details?id=' + item.Id + '&serverId=' + item.ServerId;
            var tag = item.ImageTags && item.ImageTags.Primary;
            var imgUrl = tag ? api.getScaledImageUrl(item.Id, { type: 'Primary', maxWidth: 480, quality: 96, tag: tag }) : null;
            var ratio = item.PrimaryImageAspectRatio || 16 / 9;
            var covered = !local || Math.abs(ratio - 16 / 9) / (16 / 9) <= 0.2 || tileShape(shape) !== 'landscape';
            var imageClass = 'cardImageContainer ' + (covered ? 'coveredImage' : 'containedImage') + (imgUrl ? '' : ' ' + defaultBackgroundClass(item.Name || '')) + ' cardContent';
            var lazy = imgUrl ? ' lazy" data-src="' + imgUrl + '"' : '"';
            var camera = '<div class="cardIndicators"><div class="indicator videoIndicator"><span class="material-icons indicatorIcon videocam" aria-hidden="true"></span></div></div>';
            var image = tv
                ? '<div class="' + imageClass + lazy + '>' + camera
                : '<a href="' + url + '" data-action="play" class="' + imageClass + ' itemAction' + lazy + ' aria-label="' + name + '">' + camera;
            var title = tv ? name : '<a href="' + url + '" data-id="' + item.Id + '" data-serverid="' + item.ServerId + '" data-type="' + item.Type
                + '" data-mediatype="' + item.MediaType + '" data-isfolder="false" class="itemAction textActionButton" title="' + name + '" data-action="link">' + name + '</a>';
            var footer = "<div class='cardText cardTextCentered cardText-first'><bdi>" + title + '</bdi></div>'
                + "<div class='cardText cardTextCentered'>" + (local ? (extraTypeLine(item, dictionary) ? escapeHtml(extraTypeLine(item, dictionary)) : '&nbsp;') : item.Etag ? steamLabelLink(steamLabelText(item.Etag)) : '&nbsp;') + '</div>';
            var tagName = tv ? 'button' : 'div';
            var classes = 'card ' + classesOf[0] + (layout.contains('layout-desktop') ? ' card-hoverable' : '') + (tv ? ' show-focus show-animation' : '')
                + ' card-withuserdata' + (tv ? ' itemAction' : '');
            var hover = layout.contains('layout-desktop') ? '<div class="cardOverlayContainer itemAction" data-action="play"><a href="' + url + '" class="cardImageContainer"></a>'
                + '<button is="paper-icon-button-light" class="cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light cardOverlayFab-primary" data-action="resume">'
                + '<span class="material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover play_arrow" aria-hidden="true"></span></button></div>' : '';
            return '<' + tagName + ' data-index="' + index + '"' + (tv ? ' data-action="play"' : '') + ' data-isfolder="false" data-serverid="' + item.ServerId
                + '" data-id="' + item.Id + '" data-type="' + item.Type + '" data-mediatype="' + item.MediaType + '" data-prefix="'
                + escapeHtml((item.SortName || item.Name || '').substring(0, 3).toUpperCase()) + '" class="' + classes + '"' + (tv ? ' aria-label="' + name + '"' : '') + '>'
                + '<div class="cardBox cardBox-bottompadded"><div class="cardScalable"><div class="cardPadder ' + classesOf[1] + '"></div>'
                + image + (tv ? '</div>' : '</a>') + hover + '</div>' + footer + '</div></' + tagName + '>';
        }).join('');
    }

    // The row follows the shown version: other items replace its tiles in place, no item or the switch off removes it, the same items leave it
    // untouched. Built once at its RowOrder place.
    var steamLabel = '';
    // The switches of the text lines under the tiles of Documents and Screenshots and the user's titles for four rows (set from the page's configuration in apply).
    var textOptions = { docFit: 'Adaptive', docLimit: 20, docTitle: true, docSubtext: true, docThumbs: true, serverComics: false, shotFit: 'Adaptive', shotLimit: 20, shotTitle: false, shotSubtext: false, wpFit: 'Adaptive', wpLimit: 20, wpTitle: false, wpSubtext: false };
    var labelOptions = { specials: '', screenshots: '', wallpapers: '', documents: '', soundtrack: '' };

    // The two text lines under a picture tile (title, subtext) as vanilla's cardBuilder writes them. Both off: no lines, no room. One off: its line
    // has the same markup with an invisible link, so the row is as high as with both.
    function tileTextLines(titleOn, subOn, title, sub, action) {
        if (!titleOn && !subOn) {
            return '';
        }

        var line = function (first, on, text) {
            var inner = on && text
                ? "<a class='itemAction textActionButton' data-action='" + action + "' title=\"" + escapeHtml(text) + "\">" + escapeHtml(text) + '</a>'
                : "<a class='textActionButton' aria-hidden='true' tabindex='-1' style='pointer-events:none'>&nbsp;</a>";
            return "<div class='cardText cardTextCentered" + (first ? ' cardText-first' : '') + "'><bdi>" + inner + '</bdi></div>';
        };
        return line(true, titleOn, title) + line(false, subOn, sub);
    }
    function loadSteamSpecials(view, request, id, order, enabled) {
        var api = window.ApiClient;
        view.pbSteamArgs = { id: id, order: order, enabled: enabled };
        (enabled ? api.getJSON(api.getUrl('PlayniteBridge/games/' + encodeURIComponent(id) + '/steamvideos')) : Promise.resolve([])).then(function (items) {
            if (view.pbRequest !== request) {
                return;
            }
            items = items || [];
            var key = id.replace(/-/g, '').toLowerCase();
            items.forEach(function (item) {
                extraIds[item.Id] = key;
            });
            var signature = items.map(function (item) {
                return item.Id;
            }).join(',');
            var existing = view.querySelector('#pbSteamSpecialsCollapsible');
            if (existing) {
                if (!items.length) {
                    existing.parentNode.removeChild(existing);
                } else if (existing.pbSignature !== signature) {
                    existing.pbSignature = signature;
                    existing.querySelector('.itemsContainer').innerHTML = steamCards(items);
                    fillImages(existing);
                }
                return;
            }
            if (!items.length) {
                return;
            }
            var similar = view.querySelector('#similarCollapsible');
            if (!similar) {
                return;
            }
            var holder = document.createElement('div');
            holder.innerHTML = '<div id="pbSteamSpecialsCollapsible" class="verticalSection detailVerticalSection"><h2 class="sectionTitle sectionTitle-cards padded-right"></h2>'
                + '<div is="emby-scroller" class="padded-top-focusscale padded-bottom-focusscale no-padding" data-centerfocus="true"><div is="emby-itemscontainer" class="scrollSlider focuscontainer-x itemsContainer">'
                + steamCards(items) + '</div></div></div>';
            var section = holder.firstChild;
            section.querySelector('.sectionTitle').textContent = steamLabel || 'Steam Special Features';
            section.pbSignature = signature;
            var keys = order.match(/Screenshots|Wallpapers|Documents|Steamspecials|Vrowsbefore|Specials|Videos|Soundtrack|Collections|Related|Actors|Connections|Similar/g) || [];
            var at = keys.indexOf('Steamspecials');
            var present = function (key) { return !!tileRow(view, key); };
            var next = keys.slice(at + 1).filter(present)[0];
            var prev = keys.slice(0, at).filter(present).pop();
            if (next) {
                tileRow(view, next).parentNode.insertBefore(section, tileRow(view, next));
            } else if (prev) {
                tileRow(view, prev).parentNode.insertBefore(section, tileRow(view, prev).nextSibling);
            } else {
                similar.parentNode.insertBefore(section, similar);
            }
            orderFlex(view, order);
            fillImages(section);
        }, function () {});
    }

    // The Videos rows other than Jellyfin's merged Special Features row (games/{id}/videorows): one section per type or slot that is not merged, in
    // one wrapper #pbVideoRowsCollapsible, plus the Steam row of its own when the server says steamOwn. Each section holds its first 60 tiles and a
    // dashed "Show more" card.
    function videoRowTitle(row, dictionary) {
        if (row.label) {
            return row.label;
        }
        var keys = { extras: ['Extras', 'Extras'], clips: ['VideoClips', 'Clips'], behindthescenes: ['BehindTheScenes', 'Behind the Scenes'], deletedscenes: ['VideoDeletedScenes', 'Deleted Scenes'],
            interviews: ['VideoInterviews', 'Interviews'], scenes: ['HeaderScenes', 'Scenes'], samples: ['VideoSamples', 'Samples'], shorts: ['VideoShorts', 'Shorts'], featurettes: ['VideoFeaturettes', 'Featurettes'] };
        var word = keys[row.key];
        return word ? (translate(dictionary, word[0]) || word[1]) : row.key;
    }

    function videoRowSection(row, shape, dictionary) {
        var more = row.count > row.items.length ? pictureMoreCard(dictionary, shape) : '';
        var holder = document.createElement('div');
        holder.innerHTML = '<div class="verticalSection detailVerticalSection pbVideoRow" data-pb-row="' + escapeHtml(row.key) + '"><h2 class="sectionTitle sectionTitle-cards padded-right"></h2>'
            + '<div is="emby-scroller" class="padded-top-focusscale padded-bottom-focusscale no-padding" data-centerfocus="true"><div is="emby-itemscontainer" class="scrollSlider focuscontainer-x itemsContainer">'
            + steamCards(row.items, shape, true, dictionary) + more + '</div></div></div>';
        var section = holder.firstChild;
        section.querySelector('.sectionTitle').textContent = videoRowTitle(row, dictionary);
        section.pbShown = row.items.length;
        section.pbCount = row.count;
        return section;
    }

    function loadVideoRows(view, request, id, order, configuration, dictionary) {
        var api = window.ApiClient;
        var wanted = !!(configuration && configuration.EnableVideos !== false);
        var promise = wanted ? api.getJSON(api.getUrl('PlayniteBridge/games/' + encodeURIComponent(id) + '/videorows')) : Promise.resolve(null);
        view.pbVideoRows = promise;
        promise.then(function (data) {
            if (view.pbRequest !== request) {
                return;
            }
            data = data || { native: [], steamOwn: false, shape: 'landscape', rows: [] };
            var key = id.replace(/-/g, '').toLowerCase();
            var shape = tileShape(data.shape);
            data.rows.forEach(function (row) {
                row.items.forEach(function (item) {
                    extraIds[item.Id] = key;
                });
            });
            // two wrappers: the rows that come before the merged row in the table stand above Jellyfin's Special Features row, the others below it (all above it when nothing is merged)
            [['Vrowsbefore', 'pbVideoRowsBefore', data.rows.filter(function (row) { return row.before; })], ['Videos', 'pbVideoRowsCollapsible', data.rows.filter(function (row) { return !row.before; })]].forEach(function (group) {
                var rows = group[2];
                var signature = rows.map(function (row) {
                    return row.key + ':' + row.label + ':' + row.items.map(function (item) { return item.Id; }).join(',');
                }).join('|') + '#' + shape;
                var existing = view.querySelector('#' + group[1]);
                if (existing && (!rows.length || existing.pbSignature !== signature)) {
                    existing.parentNode.removeChild(existing);
                    existing = null;
                }
                var similar = view.querySelector('#similarCollapsible');
                if (existing || !rows.length || !similar) {
                    return;
                }
                var wrap = document.createElement('div');
                wrap.id = group[1];
                wrap.pbSignature = signature;
                rows.forEach(function (row) {
                    wrap.appendChild(videoRowSection(row, shape, dictionary));
                });
                wrap.addEventListener('click', function (event) {
                    var button = event.target.closest && event.target.closest('.pbShowMoreBtn');
                    var section = button && button.closest('.pbVideoRow');
                    if (!section) {
                        return;
                    }
                    event.preventDefault();
                    showMoreVideos(section, id, shape, dictionary);
                });
                var keys = order.match(/Screenshots|Wallpapers|Documents|Steamspecials|Vrowsbefore|Specials|Videos|Soundtrack|Collections|Related|Actors|Connections|Similar/g) || [];
                var at = keys.indexOf(group[0]);
                var present = function (name) { return !!tileRow(view, name); };
                var next = keys.slice(at + 1).filter(present)[0];
                var prev = keys.slice(0, at).filter(present).pop();
                if (next) {
                    tileRow(view, next).parentNode.insertBefore(wrap, tileRow(view, next));
                } else if (prev) {
                    tileRow(view, prev).parentNode.insertBefore(wrap, tileRow(view, prev).nextSibling);
                } else {
                    similar.parentNode.insertBefore(wrap, similar);
                }
                orderFlex(view, order);
                fillImages(wrap);
            });
            loadSteamSpecials(view, request, id, order, !!data.steamOwn && !!(configuration && configuration.EnableSteamSpecialFeatures));
        }, function () {
            loadSteamSpecials(view, request, id, order, false);
        });
    }

    function showMoreVideos(section, id, shape, dictionary) {
        var api = window.ApiClient;
        if (section.pbBusy) {
            return;
        }
        section.pbBusy = true;
        api.getJSON(api.getUrl('PlayniteBridge/games/' + encodeURIComponent(id) + '/videorows', { row: section.getAttribute('data-pb-row'), from: section.pbShown })).then(function (data) {
            section.pbBusy = false;
            var row = data && data.rows && data.rows[0];
            if (!row) {
                return;
            }
            var key = id.replace(/-/g, '').toLowerCase();
            row.items.forEach(function (item) {
                extraIds[item.Id] = key;
            });
            var container = section.querySelector('.itemsContainer'), card = container.querySelector('.pbShotMore');
            var holder = document.createElement('div');
            holder.innerHTML = steamCards(row.items, shape, true, dictionary);
            while (holder.firstChild) {
                container.insertBefore(holder.firstChild, card);
            }
            section.pbShown += row.items.length;
            if (card && section.pbShown >= row.count) {
                card.parentNode.removeChild(card);
            }
            fillImages(section);
            remeasureScroller(section);
        }, function () {
            section.pbBusy = false;
        });
    }

    // The rows Related Media and Actors from the game's game.nfo (games/{id}/nfo: real Jellyfin items). Cards as collectionCards writes them, but the
    // click goes to the item's own detail page. Placed by RowOrder, built once; other items replace the tiles.
    function nfoCards(entries, dictionary, gameId) {
        var layout = document.documentElement.classList;
        var tv = layout.contains('layout-tv');
        var api = window.ApiClient;
        var width = cardWidth(tv);
        var roleText = translate(dictionary, 'PersonRole') || 'as {0}';
        return entries.map(function (entry, index) {
            var item = entry.item;
            var person = item.Type === 'Person';
            var name = escapeHtml(item.Name || '');
            var url = '#/details?id=' + item.Id + '&serverId=' + item.ServerId;
            var tag = item.ImageTags && item.ImageTags.Primary;
            var imgUrl = entry.picture ? api.getUrl('PlayniteBridge/games/' + encodeURIComponent(gameId) + '/actors/' + entry.picture.index, { tag: entry.picture.tag })
                : tag ? api.getScaledImageUrl(item.Id, {
                    type: 'Primary',
                    fillHeight: item.PrimaryImageAspectRatio ? Math.round(width / item.PrimaryImageAspectRatio) : width / (2 / 3),
                    fillWidth: width,
                    quality: 96,
                    tag: tag
                }) : null;
            var imageClass = 'cardImageContainer coveredImage' + (imgUrl ? '' : ' ' + defaultBackgroundClass(item.Name || '')) + ' cardContent';
            var lazy = imgUrl ? ' lazy" data-src="' + imgUrl + '"' : '"';
            var image = tv
                ? '<div class="' + imageClass + lazy + '>'
                : '<a href="' + url + '" data-action="link" class="' + imageClass + ' itemAction' + lazy + ' aria-label="' + name + '">';
            var title = tv ? name : '<a href="' + url + '" data-id="' + item.Id + '" data-serverid="' + item.ServerId + '" data-type="' + item.Type
                + '" data-isfolder="' + (item.IsFolder || false) + '" class="itemAction textActionButton" title="' + name + '" data-action="link">' + name + '</a>';
            var second = (person ? entry.role && escapeHtml(roleText.replace('{0}', entry.role)) : item.ProductionYear ? String(item.ProductionYear) : '') || '&nbsp;';
            var footer = "<div class='cardText cardTextCentered cardText-first'><bdi>" + title + '</bdi></div>'
                + "<div class='cardText cardTextCentered" + (person ? ' cardText-secondary' : '') + "'><bdi>" + second + '</bdi></div>';
            var tagName = tv ? 'button' : 'div';
            var classes = 'card overflowPortraitCard' + (person ? ' personCard' : '') + (layout.contains('layout-desktop') ? ' card-hoverable' : '') + (tv ? ' show-focus show-animation' : '')
                + ' card-withuserdata' + (tv ? ' itemAction' : '');
            return '<' + tagName + ' data-index="' + index + '"' + (tv ? ' data-action="link"' : '') + ' data-isfolder="' + (item.IsFolder || false)
                + '" data-serverid="' + item.ServerId + '" data-id="' + item.Id + '" data-type="' + item.Type + '" data-prefix="'
                + escapeHtml((item.SortName || item.Name || '').substring(0, 3).toUpperCase()) + '" class="' + classes + '"' + (tv ? ' aria-label="' + name + '"' : '') + '>'
                + '<div class="cardBox cardBox-bottompadded"><div class="cardScalable"><div class="cardPadder cardPadder-overflowPortrait"></div>'
                + image + (imgUrl || !person ? '' : '<span class="material-icons cardImageIcon person" aria-hidden="true"></span>') + (tv ? '</div>' : '</a>')
                + (layout.contains('layout-desktop') ? '<div class="cardOverlayContainer itemAction" data-action="link"><a href="' + url + '" class="cardImageContainer"></a><div class="cardOverlayButton-br flex">'
                    + '<button is="paper-icon-button-light" class="cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light" data-action="menu" title="More"><span class="material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover more_vert" aria-hidden="true"></span></button></div></div>' : '')
                + '</div>' + footer + '</div></' + tagName + '>';
        }).join('');
    }

    function setNfoRow(view, order, key, title, entries, dictionary, gameId) {
        var rowId = tileRows[key];
        var signature = entries.map(function (entry) {
            return entry.item.Id + (entry.role || '') + (entry.picture ? entry.picture.tag : '');
        }).join(',');
        var existing = view.querySelector('#' + rowId);
        if (existing) {
            if (!entries.length) {
                existing.parentNode.removeChild(existing);
            } else if (existing.pbSignature !== signature || existing.querySelector('.sectionTitle').textContent !== title) {
                existing.querySelector('.sectionTitle').textContent = title;
                existing.pbSignature = signature;
                existing.querySelector('.itemsContainer').innerHTML = nfoCards(entries, dictionary, gameId);
                fillImages(existing);
            }
            return;
        }
        var similar = view.querySelector('#similarCollapsible');
        if (!entries.length || !similar) {
            return;
        }
        var holder = document.createElement('div');
        holder.innerHTML = '<div id="' + rowId + '" class="verticalSection detailVerticalSection"><h2 class="sectionTitle sectionTitle-cards padded-right"></h2>'
            + '<div is="emby-scroller" class="padded-top-focusscale padded-bottom-focusscale no-padding" data-centerfocus="true"><div is="emby-itemscontainer" class="scrollSlider focuscontainer-x itemsContainer">'
            + nfoCards(entries, dictionary, gameId) + '</div></div></div>';
        var section = holder.firstChild;
        section.querySelector('.sectionTitle').textContent = title;
        section.pbSignature = signature;
        var keys = order.match(/Screenshots|Wallpapers|Documents|Steamspecials|Vrowsbefore|Specials|Videos|Soundtrack|Collections|Related|Actors|Connections|Similar/g) || [];
        var at = keys.indexOf(key);
        var present = function (other) { return !!tileRow(view, other); };
        var next = keys.slice(at + 1).filter(present)[0];
        var prev = keys.slice(0, at).filter(present).pop();
        if (next) {
            tileRow(view, next).parentNode.insertBefore(section, tileRow(view, next));
        } else if (prev) {
            tileRow(view, prev).parentNode.insertBefore(section, tileRow(view, prev).nextSibling);
        } else {
            similar.parentNode.insertBefore(section, similar);
        }
        orderFlex(view, order);
        fillImages(section);
    }

    // Soundtrack: the audio files of the game's "soundtrack" folder as one album card in a row titled Soundtrack, placed by RowOrder. A click opens
    // the tracks beside it, a second closes them. A click on a track plays Jellyfin's "play all from here". Remote players cannot be switched off
    // from here.
    var soundtrackIds = {};

    // The track the player has loaded: an audio element with a track's route that plays or is paused part way; a stop empties its source (emptied), an end sets ended.
    function loadedSoundtrackId() {
        var loaded = [].filter.call(document.querySelectorAll('audio'), function (m) {
            return !m.ended && (!m.paused || m.currentTime > 0) && /\/Audio\/[0-9a-f-]{32,36}\//i.test(m.currentSrc);
        })[0];
        var match = loaded && /\/Audio\/([0-9a-f-]{32,36})\//i.exec(loaded.currentSrc);
        return match ? match[1].replace(/-/g, '').toLowerCase() : '';
    }

    function soundtrackCards(tracks, fresh) {
        var layout = document.documentElement.classList;
        var api = window.ApiClient;
        var loaded = loadedSoundtrackId();
        return tracks.map(function (item, index) {
            var name = escapeHtml((item.IndexNumber ? item.IndexNumber + '. ' : '') + (item.Name || ''));
            var tag = item.ImageTags && item.ImageTags.Primary;
            var imgUrl = tag ? api.getScaledImageUrl(item.Id, { type: 'Primary', maxWidth: 400, quality: 96, tag: tag }) : null;
            var imageClass = 'cardImageContainer coveredImage' + (imgUrl ? '' : ' ' + defaultBackgroundClass(item.Name || '')) + ' cardContent itemAction';
            var lazy = imgUrl ? ' lazy" data-src="' + imgUrl + '"' : '"';
            var current = item.Id.replace(/-/g, '').toLowerCase() === loaded;
            // The text lines as vanilla's cardBuilder writes them; a click on the text plays the track.
            var artists = escapeHtml((item.Artists || []).join(', '));
            var footer = "<div class='cardText cardTextCentered cardText-first'><bdi><a class='itemAction textActionButton' data-action='playallfromhere' title=\"" + name + "\">" + name + '</a></bdi></div>'
                + "<div class='cardText cardTextCentered'><bdi>" + (artists ? "<a class='itemAction textActionButton' data-action='playallfromhere' title=\"" + artists + "\">" + artists + '</a>' : "<a class='itemAction textActionButton' data-action='playallfromhere'>&nbsp;</a>") + '</bdi></div>';
            var classes = 'card overflowSquareCard pbTrack' + (layout.contains('layout-desktop') ? ' card-hoverable' : '') + (layout.contains('layout-tv') ? ' show-focus show-animation' : '')
                + (current ? ' pbTrackLoaded' : '') + (fresh ? ' pbTrackIn' : '');
            var hover = layout.contains('layout-desktop') ? '<div class="cardOverlayContainer itemAction" data-action="playallfromhere">'
                + '<button is="paper-icon-button-light" class="cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light cardOverlayFab-primary" data-action="playallfromhere" aria-label="' + name + '">'
                + '<span class="material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover play_arrow" aria-hidden="true"></span></button></div>' : '';
            return '<div data-index="' + index + '" data-isfolder="false" data-serverid="' + item.ServerId + '" data-id="' + item.Id + '" data-type="Audio" data-mediatype="Audio" data-prefix="'
                + escapeHtml((item.SortName || item.Name || '').substring(0, 3).toUpperCase()) + '" class="' + classes + '"' + (current ? ' aria-current="true"' : '') + '>'
                + '<div class="cardBox cardBox-bottompadded"><div class="cardScalable"><div class="cardPadder cardPadder-overflowSquare"></div>'
                + '<button type="button" data-action="playallfromhere" class="' + imageClass + lazy + ' aria-label="' + name + '"></button>'
                + hover + '</div>' + footer + '</div></div>';
        }).join('');
    }

    // The album card: the cover of the first track with a picture, no visible text (its name is the accessible one), a button that opens and closes
    // the tracks; aria-expanded is its only open marker. row.pbOpen is its state.
    function albumCardHtml(row) {
        var layout = document.documentElement.classList;
        var api = window.ApiClient;
        var cover = (row.pbTracks || []).filter(function (item) { return item.ImageTags && item.ImageTags.Primary; })[0];
        var imgUrl = cover ? api.getScaledImageUrl(cover.Id, { type: 'Primary', maxWidth: 400, quality: 96, tag: cover.ImageTags.Primary }) : null;
        var name = escapeHtml(row.pbName || 'Soundtrack');
        var imageClass = 'cardImageContainer coveredImage' + (imgUrl ? '' : ' ' + defaultBackgroundClass(row.pbName || 'Soundtrack')) + ' cardContent pbAlbumButton';
        var lazy = imgUrl ? ' lazy" data-src="' + imgUrl + '"' : '"';
        return '<div class="card overflowSquareCard pbAlbum' + (layout.contains('layout-desktop') ? ' card-hoverable' : '') + (layout.contains('layout-tv') ? ' show-focus show-animation' : '')
            + '"><div class="cardBox cardBox-bottompadded"><div class="cardScalable"><div class="cardPadder cardPadder-overflowSquare"></div>'
            + '<button type="button" class="' + imageClass + lazy + ' aria-label="' + name + '" aria-expanded="' + (row.pbOpen ? 'true' : 'false') + '"></button>'
            + (layout.contains('layout-desktop') ? '<div class="cardOverlayContainer"></div>' : '') + '</div>'
            + "<div class='cardText cardTextCentered cardText-first'><bdi><a class='textActionButton' aria-hidden='true' tabindex='-1' style='pointer-events:none'>&nbsp;</a></bdi></div>"
            + "<div class='cardText cardTextCentered'><bdi><a class='textActionButton' aria-hidden='true' tabindex='-1' style='pointer-events:none'>&nbsp;</a></bdi></div></div></div>";
    }

    // The container's own hooks for Jellyfin: fetchData (play all from here, and every refresh of the container) and getItemsHtml (the refresh's redraw). Jellyfin's
    // detach (a moved row) resets both, so a capturing click on the row sets them again before Jellyfin's click handler runs.
    function bindSoundtrack(row) {
        var container = row.querySelector('.itemsContainer');
        if (!container || !row.pbTracks) {
            return;
        }
        container.fetchData = function () {
            var items = row.pbTracks.slice();
            return Promise.resolve({ Items: items, TotalRecordCount: items.length });
        };
        container.getItemsHtml = function () {
            return albumCardHtml(row) + (row.pbOpen ? soundtrackCards(row.pbTracks, false) : '');
        };
    }

    function markPlayingTrack() {
        var loaded = loadedSoundtrackId();
        [].forEach.call(document.querySelectorAll('.pbTrack[data-id]'), function (card) {
            var current = card.getAttribute('data-id').replace(/-/g, '').toLowerCase() === loaded;
            card.classList.toggle('pbTrackLoaded', current);
            if (current) {
                card.setAttribute('aria-current', 'true');
            } else {
                card.removeAttribute('aria-current');
            }
        });
    }

    ['play', 'pause', 'ended', 'emptied', 'loadstart'].forEach(function (type) {
        document.addEventListener(type, function (e) {
            if (e.target instanceof HTMLAudioElement) {
                markPlayingTrack();
            }
        }, true);
    });

    // View only: the now-playing bar's heart gets class pbExtraRating (hidden by #pbStyles) while its data-id is a soundtrack track. Jellyfin writes
    // the button anew for every track, so the bar's container is watched.
    function watchBarRating() {
        var box = document.querySelector('.nowPlayingBarUserDataButtons');
        if (!box) {
            if (!watchBarRating.waiting) {
                watchBarRating.waiting = new MutationObserver(function () {
                    if (document.querySelector('.nowPlayingBarUserDataButtons')) {
                        watchBarRating.waiting.disconnect();
                        watchBarRating.waiting = null;
                        watchBarRating();
                    }
                });
                watchBarRating.waiting.observe(document.body, { childList: true, subtree: true });
            }
            return;
        }
        if (box.pbRatingObserver) {
            return;
        }
        var update = function () {
            var track = false;
            [].forEach.call(box.querySelectorAll('button[data-id]'), function (button) {
                var own = !!soundtrackIds[button.getAttribute('data-id').replace(/-/g, '').toLowerCase()];
                button.classList.toggle('pbExtraRating', own);
                track = track || own;
            });
            // The "..." menu of the bar (playlist, instant mix, metadata and image editors) works on real items only; the bar has its own stop button.
            var bar = box.closest('.nowPlayingBar');
            if (bar) {
                bar.classList.toggle('pbSoundtrackPlaying', track);
            }
        };
        box.pbRatingObserver = new MutationObserver(update);
        box.pbRatingObserver.observe(box, { childList: true });
        update();
    }

    document.addEventListener('play', function (e) {
        if (e.target instanceof HTMLAudioElement) {
            watchBarRating();
        }
    }, true);

    function soundtrackRowHtml() {
        return '<div id="pbSoundtrackCollapsible" class="verticalSection detailVerticalSection"><h2 class="sectionTitle sectionTitle-cards padded-right"></h2>'
            + '<div is="emby-scroller" class="padded-top-focusscale padded-bottom-focusscale no-padding" data-centerfocus="true"><div is="emby-itemscontainer" class="scrollSlider focuscontainer-x itemsContainer"></div></div></div>';
    }

    // Jellyfin Web's scroll arrow takes the width of the FIRST child of the slider for every child, so a row whose first child is a divider or whose
    // tiles differ in width slides nowhere. For such rows the click is answered here from the real boxes of the children (right: the first child cut
    // off at the right edge goes to the left edge; left mirrors it), as vanilla calls slideTo(position, false, undefined).
    function arrowTarget(frame, direction) {
        var slider = frame.getScrollSlider && frame.getScrollSlider();
        if (!slider) {
            return null;
        }

        var items = [].filter.call(slider.children, function (child) { return child.offsetWidth > 0; });
        if (!items.length) {
            return null;
        }

        var rtl = window.getComputedStyle(frame).direction === 'rtl';
        var box = slider.getBoundingClientRect();
        var frameStyle = window.getComputedStyle(frame), sliderStyle = window.getComputedStyle(slider);
        var windowWidth = frame.offsetWidth - (parseFloat(frameStyle.paddingLeft) || 0) - (parseFloat(frameStyle.paddingRight) || 0)
            - (parseFloat(sliderStyle.paddingLeft) || 0) - (parseFloat(sliderStyle.paddingRight) || 0);
        var position = Math.abs(frame.getScrollPosition ? frame.getScrollPosition() : 0);
        var last = Math.max((frame.getScrollSize ? frame.getScrollSize() : slider.scrollWidth) - windowWidth, 0);
        var spans = items.map(function (child) {
            var b = child.getBoundingClientRect();
            var start = rtl ? box.right - b.right : b.left - box.left;
            return [start, start + b.width];
        });
        var target = null, i;
        if (direction === 'right') {
            for (i = 0; i < spans.length && target === null; i++) {
                if (spans[i][1] > position + windowWidth + 1) {
                    target = spans[i][0] > position + 1 ? spans[i][0] : position + windowWidth;
                }
            }

            target = Math.min(target === null ? last : target, last);
        } else {
            target = 0;
            for (i = spans.length - 1; i >= 0; i--) {
                if (spans[i][0] < position - 1) {
                    target = spans[i][1] - windowWidth;
                    break;
                }
            }

            target = target < position ? Math.max(target, 0) : Math.max(position - windowWidth, 0);
        }

        return target * (rtl ? -1 : 1);
    }

    // One listener for the page (capture phase, before the button's own handler): a click on a scroll arrow whose slider has children of different widths is answered here; rows whose children
    // are all equally wide (cast, More Like This, the soundtrack ...) keep Jellyfin's own handling.
    function onArrowClick(e) {
        var button = e.target && e.target.closest ? e.target.closest('.emby-scrollbuttons-button') : null;
        var buttons = button && button.parentNode;
        var frame = buttons && buttons.nextSibling;
        if (!button || button.disabled || !frame || !frame.getScrollSlider) {
            return;
        }

        var slider = frame.getScrollSlider();
        var widths = slider ? [].map.call(slider.children, function (child) { return child.offsetWidth; }).filter(function (w) { return w > 0; }) : [];
        if (widths.length < 2 || widths.every(function (w) { return Math.abs(w - widths[0]) <= 1; })) {
            return;
        }

        var target = arrowTarget(frame, button.getAttribute('data-direction'));
        if (target === null) {
            return;
        }

        e.stopImmediatePropagation();
        if (frame.slideTo) {
            frame.slideTo(target, false, undefined);
        } else if (frame.scrollToPosition) {
            frame.scrollToPosition(target);
        }
    }

    document.addEventListener('click', onArrowClick, true);

    // Reader controls. A document opened from one of our tiles opens in Jellyfin's PDF, EPUB or comics reader, which lack some controls (PDF: all but
    // Close; EPUB: previous/next and position; comics: fullscreen). We add the missing ones to the open reader (never in Jellyfin's own Books
    // library) and remove them when it closes. The reader plugin is reached through webpack's module list; without it the buttons send the reader's
    // own keys.
    var READER_IDS = { pdfplayer: 'pdfPlayer', bookplayer: 'bookPlayer', comicsplayer: 'comicsPlayer' };
    var readerOwn = null;     // the tile of ours that was clicked last: { id, at }
    var readerWatch = null;   // the observer that waits for the reader's dialog after such a click
    var reader = null;        // the controls of the open reader
    var readerManager = null; // Jellyfin's playbackManager, once found
    var READER_ZOOMS = [1, 1.5, 2, 3];

    var readerManagerTried = 0;   // time of the last lookup that found nothing: the several MB of module source are not searched again within a minute

    function findPlaybackManager() {
        if (readerManager || Date.now() - readerManagerTried < 60000) {
            return readerManager;
        }

        var require = null;
        try {
            window.webpackChunk.push([['pbReader' + Math.random()], {}, function (r) { require = r; }]);
            Object.keys(require.m).some(function (key) {
                var source = String(require.m[key]);
                if (source.indexOf('getPlayers') === -1 || source.indexOf('playbackstart') === -1) {
                    return false;
                }

                var exported = require(key);
                return Object.keys(exported).some(function (name) {
                    if (exported[name] && typeof exported[name].getPlayers === 'function') {
                        readerManager = exported[name];
                        return true;
                    }

                    return false;
                });
            });
        } catch (e) {
        }

        readerManagerTried = readerManager ? 0 : Date.now();
        return readerManager;
    }

    function readerIcon(name) {
        return '<span class="material-icons actionButtonIcon ' + name + '" aria-hidden="true"></span>';
    }

    function readerButton(cls, name, text) {
        return '<button is="paper-icon-button-light" class="autoSize bookplayerButton hide-mouse-idle-tv ' + cls + '" tabindex="0" title="' + escapeHtml(text) + '" aria-label="' + escapeHtml(text) + '">' + readerIcon(name) + '</button>';
    }

    function attachReader(dialog, id) {
        if (!readerOwn) {
            return;
        }

        // the EPUB failure test reads the resource timing of the download: a page that has been open for long has a full buffer, so it is emptied here and then given room for 1000 entries
        try {
            performance.clearResourceTimings();
            performance.setResourceTimingBufferSize(1000);
        } catch (e) {
        }

        var current = { dialog: dialog, id: id, zoom: 0, timer: 0, style: null, update: function () { }, teardown: function () { if (reader === current) { reader = null; } },
            player: function () {
                // the plugin object that started this book (readerOwn.plugin, see wrapReaderPlugins), else the registered one of the dialog's kind
                if (readerOwn && readerOwn.plugin) {
                    return readerOwn.plugin;
                }

                var manager = findPlaybackManager();
                var players = manager && manager.getPlayers ? manager.getPlayers() : [];
                return players.filter(function (player) { return player.id === id.toLowerCase(); })[0] || null;
            } };
        reader = current;
        loadStrings().then(function (dictionary) {
            if (reader === current && dialog.isConnected) {
                buildReader(current, dictionary);
                if (readerOwn && readerOwn.failed) {
                    failReader(dialog);
                }
            }
        });
    }

    // A document that cannot be opened (damaged, password protected, not a document) leaves Jellyfin's readers open and empty. The reader plugins'
    // play() is wrapped once, on a click on one of our tiles: a rejected promise, an empty archive or a first page that does not load end in a
    // message and the reader is closed through its own Close button.
    function failReader(dialog) {
        if (!dialog || !dialog.isConnected || dialog.pbFailed) {
            return;
        }

        dialog.pbFailed = true;
        var api = window.ApiClient;
        var allowed = api && api.getCurrentUser ? api.getCurrentUser().then(function (user) {
            return !(user && user.Policy && user.Policy.EnableContentDownloading === false);
        }, function () { return true; }) : Promise.resolve(true);
        Promise.all([loadStrings(), allowed]).then(function (results) {
            var dictionary = results[0];
            if (!results[1]) {
                showReaderToast(translate(dictionary, 'ReaderDownloadNotAllowed') || 'This account is not allowed to download media, which the reader needs.');
            } else {
                showReaderToast(translate(dictionary, 'ReaderOpenFailed') || "Jellyfin's reader could not open this document. It may be damaged, password protected or not supported.",
                    api && readerOwn ? { text: translate(dictionary, 'Download') || 'Download', href: tokenUrl(api, 'Items/' + readerOwn.id + '/Download') } : null);
            }

            // the plugin's own stop closes the dialog; its Close button is wired only after the book loaded (an EPUB that never loaded has none)
            var plugin = readerOwn && readerOwn.plugin;
            if (plugin && typeof plugin.stop === 'function') {
                try {
                    plugin.stop();
                } catch (e) {
                }
            }

            setTimeout(function () {
                var exit = dialog.isConnected && dialog.querySelector('.btnExit, #btnBookplayerExit');
                if (exit) {
                    exit.click();
                }
            }, 700);
        });
    }

    function wrapReaderPlugins() {
        var manager = findPlaybackManager();
        var plugins = manager && manager.getPlayers ? manager.getPlayers() : [];
        plugins.forEach(function (plugin) {
            if (!READER_IDS[plugin.id] || plugin.pbWrapped || typeof plugin.play !== 'function') {
                return;
            }

            plugin.pbWrapped = true;
            var play = plugin.play;
            plugin.play = function () {
                var result = play.apply(this, arguments);
                var self = this;
                if (readerOwn && Date.now() - readerOwn.at < 15000) {
                    readerOwn.plugin = self;
                    readerOwn.failed = false;
                    var fail = function () {
                        readerOwn.failed = true;
                        failReader(document.getElementById(READER_IDS[self.id]));
                    };
                    if (result && typeof result.then === 'function') {
                        result.then(function () {
                            if (self.id === 'comicsplayer' && !(self.pageCount > 0)) {
                                fail();
                            }
                        }, fail);
                    }
                }

                return result;
            };
        });
    }

    // Jellyfin 12 mounts its own React reader bar (BookOsd: back, previous, next, fullscreen) in all three readers; 10.10.7 has none. On 12 only what
    // the bar lacks is built (page counter, PDF zoom keys, spinner); on 10.10.7 everything.
    function readerPlan(osd, pdf, epub, fullscreenEnabled) {
        return { fullscreenButton: !osd && !epub && !!fullscreenEnabled, zoomButtons: !osd && pdf, edges: !osd && (pdf || epub), count: pdf || epub };
    }

    function buildReader(c, dictionary) {
        var dialog = c.dialog;
        var text = function (key, fallback) {
            return translate(dictionary, key) || fallback;
        };
        var texts = { previous: text('ReaderPrevious', 'Previous page'), next: text('ReaderNext', 'Next page'), zoomIn: text('ReaderZoomIn', 'Zoom in'), zoomOut: text('ReaderZoomOut', 'Zoom out'),
            fit: text('ReaderZoomFit', 'Fit to window'), fullscreen: text('ReaderFullscreen', 'Fullscreen'), exitFullscreen: text('ReaderExitFullscreen', 'Exit fullscreen') };
        var pdf = c.id === 'pdfPlayer';
        var epub = c.id === 'bookPlayer';
        var root = '#' + c.id;
        var plan = readerPlan(!!dialog.querySelector('#bookOsdMount'), pdf, epub, document.fullscreenEnabled);

        var exitIcon = dialog.querySelector('.btnExit .material-icons, #btnBookplayerExit .material-icons');
        var rgb = (exitIcon ? getComputedStyle(exitIcon).color : 'rgb(255, 255, 255)').match(/\d+/g) || [255, 255, 255];
        var light = (Number(rgb[0]) * 299 + Number(rgb[1]) * 587 + Number(rgb[2]) * 114) / 1000 > 128;
        var chip = light ? 'rgba(0,0,0,.5)' : 'rgba(255,255,255,.7)';
        var ink = light ? '#fff' : '#000';
        c.style = document.createElement('style');
        c.style.textContent =
            root + ' .pbReaderEdge{position:absolute;top:50%;transform:translateY(-50%);z-index:1002;width:3.2em;height:3.2em;margin:0 .5vh;border-radius:50%;background:' + chip + ';color:' + ink + '}' +
            root + ' .pbReaderEdge .actionButtonIcon{color:' + ink + ';opacity:.85;font-size:2em}' +
            root + ' .pbReaderPrev{left:0}' + root + ' .pbReaderNext{right:0}' +
            root + ' .pbReaderBar [hidden],' + root + ' .pbReaderEdge[hidden],' + root + ' .pbReaderCount[hidden]{display:none !important}' +
            root + ' .pbReaderCount{position:absolute;bottom:1vh;left:50%;transform:translateX(-50%);z-index:1002;background:' + chip + ';color:' + ink + ';padding:2px 8px;border-radius:4px;' +
            'pointer-events:none;font-size:.95em;white-space:nowrap}' +
            root + ' .pbReaderSpin{position:absolute;top:50%;left:50%;width:3em;height:3em;margin:-1.5em 0 0 -1.5em;border:.25em solid rgba(128,128,128,.35);border-top-color:var(--theme-primary-color,#00a4dc);border-radius:50%;' +
            'animation:pbReaderSpin 1s linear infinite;z-index:1001;pointer-events:none}' +
            '@keyframes pbReaderSpin{to{transform:rotate(360deg)}}' +
            root + ' .pbReaderSpin[hidden]{display:none !important}' +
            root + ' .pbReaderBar button{background:' + chip + ';border-radius:50%;margin-left:.4em}' +
            root + ' .pbReaderBar button:disabled{opacity:.35}' +
            '#pdfPlayer.pbReaderZoomed{overflow:auto !important}' +
            '#pdfPlayer.pbReaderZoomed #canvas{height:calc(100% * var(--pbz)) !important;width:auto !important;max-width:none}' +
            '@media (orientation:portrait){#pdfPlayer.pbReaderZoomed #canvas{width:calc(100% * var(--pbz)) !important;height:auto !important}}';
        document.head.appendChild(c.style);

        var bar = dialog.querySelector('.actionButtons');
        var extra = '';
        if (plan.fullscreenButton) {
            extra += readerButton('pbReaderFullscreen', 'fullscreen', texts.fullscreen + ' (F)');
        }

        if (plan.zoomButtons) {
            extra += readerButton('pbReaderZoomOut', 'zoom_out', texts.zoomOut + ' (-)') + readerButton('pbReaderFit', 'zoom_out_map', texts.fit + ' (0)') + readerButton('pbReaderZoomIn', 'zoom_in', texts.zoomIn + ' (+)');
        }

        if (bar && extra) {
            bar.classList.add('pbReaderBar');
            bar.insertAdjacentHTML('afterbegin', extra);
        }

        var edges = null;
        var count = null;
        if (plan.count) {
            var holder = document.createElement('div');
            holder.className = 'pbReaderNav';
            holder.innerHTML = (plan.edges ? readerButton('pbReaderEdge pbReaderPrev', 'chevron_left', texts.previous) + readerButton('pbReaderEdge pbReaderNext', 'chevron_right', texts.next) : '')
                + '<div class="pbReaderCount" dir="ltr" aria-live="polite" hidden></div>';
            dialog.appendChild(holder);
            edges = plan.edges ? { prev: holder.querySelector('.pbReaderPrev'), next: holder.querySelector('.pbReaderNext') } : null;
            count = holder.querySelector('.pbReaderCount');
        }

        // the reader's own key: its handler sits on the document and also covers what the plugin checks (loaded, modifiers)
        // a document that is still being read (a large PDF can take half a minute) after Jellyfin's own spinner went: a spinner says it is working
        var spin = document.createElement('div');
        spin.className = 'pbReaderSpin';
        spin.hidden = true;
        dialog.appendChild(spin);
        var turn = function (key) {
            document.dispatchEvent(new KeyboardEvent('keydown', { key: key, bubbles: true, cancelable: true }));
            setTimeout(c.update, 60);
        };

        var setZoom = function (index) {
            c.zoom = Math.max(0, Math.min(READER_ZOOMS.length - 1, index));
            dialog.style.setProperty('--pbz', String(READER_ZOOMS[c.zoom]));
            dialog.classList.toggle('pbReaderZoomed', c.zoom > 0);
            c.update();
        };

        var onClick = function (e) {
            var button = e.target.closest && e.target.closest('button');
            if (!button || !dialog.contains(button)) {
                return;
            }

            if (button.classList.contains('pbReaderPrev')) {
                turn('ArrowLeft');
            } else if (button.classList.contains('pbReaderNext')) {
                turn('ArrowRight');
            } else if (button.classList.contains('pbReaderZoomIn')) {
                setZoom(c.zoom + 1);
            } else if (button.classList.contains('pbReaderZoomOut')) {
                setZoom(c.zoom - 1);
            } else if (button.classList.contains('pbReaderFit')) {
                setZoom(0);
            } else if (button.classList.contains('pbReaderFullscreen')) {
                if (document.fullscreenElement) {
                    document.exitFullscreen();
                } else {
                    document.documentElement.requestFullscreen().catch(function () { });
                }
            }
        };
        dialog.addEventListener('click', onClick);

        // the PDF reader turns the page on every touch (left half back, right half forward): not on our buttons, and not while a zoomed page is being
        // moved
        var onTouch = function (e) {
            var target = e.target;
            if ((target.closest && target.closest('.pbReaderBar, .pbReaderEdge')) || (pdf && c.zoom > 0 && dialog.contains(target))) {
                e.stopPropagation();
            }
        };
        document.addEventListener('touchstart', onTouch, true);

        var onImageError = function (e) {
            var player = c.player();
            if (c.id === 'comicsPlayer' && e.target && e.target.tagName === 'IMG' && player && player.pageCount === 1) {
                failReader(dialog);
            }
        };
        dialog.addEventListener('error', onImageError, true);
        // an error that nothing handles while a PDF or EPUB has not loaded yet (epub.js throws inside a promise chain nobody waits for, so play() never settles)
        var onFault = function (e) {
            var player = c.player();
            var source = e && (e.filename || (e.reason && e.reason.stack) || (e.error && e.error.stack)) || '';
            if (epub && player && !player.loaded && /epubjs/.test(String(source))) {
                failReader(dialog);
            }
        };
        window.addEventListener('unhandledrejection', onFault);
        window.addEventListener('error', onFault);
        // the focus enters the reader and stays in it (Tab circles through its buttons), Escape closes it also while it loads, + - 0 zoom and f is fullscreen
        dialog.setAttribute('tabindex', '-1');
        dialog.focus({ preventScroll: true });
        var closeName = translate(dictionary, 'ButtonClose') || 'Close';
        [].forEach.call(dialog.querySelectorAll('.btnExit, #btnBookplayerExit'), function (close) {
            close.setAttribute('aria-label', closeName);
            close.title = closeName;
        });
        var onKeys = function (e) {
            if (!dialog.isConnected || e.ctrlKey || e.altKey || e.metaKey) {
                return;
            }

            var player = c.player();
            if (e.key === 'Tab') {
                var items = [].filter.call(dialog.querySelectorAll('button, [tabindex="0"]'), function (item) { return !item.hidden && !item.disabled && item.offsetParent !== null; });
                if (!items.length) {
                    return;
                }

                var at = items.indexOf(document.activeElement);
                var next = e.shiftKey ? (at <= 0 ? items.length - 1 : at - 1) : (at < 0 || at === items.length - 1 ? 0 : at + 1);
                e.preventDefault();
                items[next].focus();
            } else if (e.key === 'Escape' && player && typeof player.stop === 'function' && (pdf || epub ? !player.loaded : !(player.pageCount > 0))) {
                e.preventDefault();
                player.stop();
            } else if (pdf && !e.shiftKey && (e.key === '+' || e.key === '=')) {
                setZoom(c.zoom + 1);
            } else if (pdf && !e.shiftKey && e.key === '-') {
                setZoom(c.zoom - 1);
            } else if (pdf && e.key === '0') {
                setZoom(0);
            } else if (!epub && (e.key === 'f' || e.key === 'F') && document.fullscreenEnabled) {
                var full = dialog.querySelector('.pbReaderFullscreen');
                if (full) {
                    full.click();
                }
            }
        };
        document.addEventListener('keydown', onKeys);
        var onFullscreen = function () {
            var button = dialog.querySelector('.pbReaderFullscreen');
            if (button) {
                var on = !!document.fullscreenElement;
                button.querySelector('.material-icons').className = 'material-icons actionButtonIcon ' + (on ? 'fullscreen_exit' : 'fullscreen');
                button.title = (on ? texts.exitFullscreen : texts.fullscreen) + ' (F)';
                button.setAttribute('aria-label', button.title);
            }
        };
        document.addEventListener('fullscreenchange', onFullscreen);

        c.update = function () {
            var player = c.player();
            var low = false;
            var high = false;
            var label = '';
            if (pdf && player && player.loaded) {
                var total = typeof player.duration === 'function' ? player.duration() : 0;
                var page = player.progress || 0;
                low = page > 0;
                high = page < total - 1;
                label = total ? (page + 1) + ' / ' + total : '';
            } else if (epub && player && player.rendition && player.loaded) {
                var location = player.rendition.currentLocation && player.rendition.currentLocation();
                if (location && location.start) {
                    var rtl = player.rendition.book && player.rendition.book.package && player.rendition.book.package.metadata && player.rendition.book.package.metadata.direction === 'rtl';
                    low = !(rtl ? location.atEnd : location.atStart);
                    high = !(rtl ? location.atStart : location.atEnd);
                    var locations = player.rendition.book && player.rendition.book.locations;
                    var percent = locations && locations.total ? locations.percentageFromCfi(location.start.cfi) : null;
                    label = typeof percent === 'number' && isFinite(percent) ? Math.round(percent * 100) + ' %' : '';
                }
            } else if ((pdf || epub) && !player) {
                low = true;
                high = true;
            }

            if (edges) {
                edges.prev.hidden = !low;
                edges.next.hidden = !high;
            }

            if (count) {
                count.hidden = !label;
                if (label && count.textContent !== label) {
                    count.textContent = label;
                }
            }

            // Jellyfin's readers show their own spinner (loading.show(), .docspinner) while they open: ours only fills the time after it went and before the first page, never beside it
            var vanillaSpinner = !!document.querySelector('.docspinner.mdlSpinnerActive');
            spin.hidden = vanillaSpinner || !(player && (pdf || epub ? !player.loaded : c.id === 'comicsPlayer' && !(player.pageCount > 0)));
            var zoomIn = dialog.querySelector('.pbReaderZoomIn');
            if (zoomIn) {
                zoomIn.disabled = c.zoom >= READER_ZOOMS.length - 1;
                dialog.querySelector('.pbReaderZoomOut').disabled = c.zoom <= 0;
                dialog.querySelector('.pbReaderFit').disabled = c.zoom <= 0;
            }
        };

        var later = function () { setTimeout(c.update, 80); };
        document.addEventListener('keydown', later);
        dialog.addEventListener('touchend', later);
        c.teardown = function () {
            clearInterval(c.timer);
            document.removeEventListener('keydown', later);
            document.removeEventListener('keydown', onKeys);
            dialog.removeEventListener('touchend', later);
            dialog.removeEventListener('click', onClick);
            dialog.removeEventListener('error', onImageError, true);
            window.removeEventListener('unhandledrejection', onFault);
            window.removeEventListener('error', onFault);
            document.removeEventListener('touchstart', onTouch, true);
            document.removeEventListener('fullscreenchange', onFullscreen);
            if (c.style && c.style.parentNode) {
                c.style.parentNode.removeChild(c.style);
            }

            if (reader === c) {
                reader = null;
            }
        };

        c.update();
        // the position changes with every page turn; the timer catches the first page and the EPUB's late locations, and ends the controls when the
        // dialog is gone
        var started = Date.now();
        c.timer = setInterval(function () {
            if (!dialog.isConnected) {
                c.teardown();
            } else {
                c.update();
                // an EPUB that epub.js neither opens nor refuses (a damaged archive) spins for good: 30 s after its download ended it counts as failed
                var player = c.player();
                if (epub && player && !player.loaded && Date.now() - started > 30000) {
                    var id = readerOwn ? readerOwn.id : '';
                    var done = performance.getEntriesByType('resource').filter(function (entry) {
                        return id && entry.name.toLowerCase().indexOf('/items/' + id + '/download') !== -1 && entry.responseEnd > 0;
                    }).pop();
                    if (done && performance.now() - done.responseEnd > 30000) {
                        failReader(dialog);
                    }
                }
            }
        }, 500);
    }

    function watchReader() {
        if (readerWatch) {
            readerWatch.disconnect();
        }

        var found = function () {
            var id = Object.keys(READER_IDS).filter(function (key) {
                return document.getElementById(READER_IDS[key]);
            })[0];
            var dialog = id && document.getElementById(READER_IDS[id]);
            if (dialog && !(reader && reader.dialog === dialog)) {
                if (readerWatch) {
                    readerWatch.disconnect();
                    readerWatch = null;
                }

                clearTimeout(watchReader.timer);
                attachReader(dialog, READER_IDS[id]);
                return true;
            }

            return false;
        };
        if (found()) {
            return;
        }

        readerWatch = new MutationObserver(found);
        readerWatch.observe(document.body, { childList: true });
        watchReader.timer = setTimeout(function () {
            if (readerWatch) {
                readerWatch.disconnect();
                readerWatch = null;
            }
        }, 15000);
    }

    // Jellyfin's readers load the book from Items/{id}/Download, which the server refuses (403) to an account without "Allow media downloads",
    // leaving the reader loading for good. The current user's policy is read once per documents row; while it says no, a tile shows a message
    // instead.
    var downloadAllowed = null;

    function loadDownloadPolicy() {
        var api = window.ApiClient;
        if (api && api.getCurrentUser) {
            api.getCurrentUser().then(function (user) {
                downloadAllowed = !(user && user.Policy && user.Policy.EnableContentDownloading === false);
            }, function () { });
        }
    }

    function showReaderToast(message, link) {
        var container = document.querySelector('.toastContainer');
        if (!container) {
            container = document.createElement('div');
            container.className = 'toastContainer';
            document.body.appendChild(container);
        }

        var toast = document.createElement('div');
        toast.className = 'toast';
        toast.setAttribute('role', 'status');
        toast.textContent = message;
        if (link) {
            var anchor = document.createElement('a');
            anchor.href = link.href;
            anchor.textContent = link.text;
            anchor.setAttribute('download', '');
            anchor.style.cssText = 'margin-left:1em;color:inherit;text-decoration:underline;pointer-events:auto';
            toast.appendChild(anchor);
        }

        container.appendChild(toast);
        setTimeout(function () { toast.classList.add('toastVisible'); }, 300);
        setTimeout(function () {
            toast.classList.remove('toastVisible');
            setTimeout(function () { toast.parentNode && toast.parentNode.removeChild(toast); }, 300);
        }, link ? 9300 : 4300);
    }

    document.addEventListener('click', function (e) {
        var card = e.target && e.target.closest ? e.target.closest('#pbDocumentsCollapsible .card[data-id]') : null;
        if (!card) {
            return;
        }

        if (downloadAllowed === false) {
            e.stopImmediatePropagation();
            e.preventDefault();
            loadStrings().then(function (dictionary) {
                showReaderToast(translate(dictionary, 'ReaderDownloadNotAllowed') || 'This account is not allowed to download media, which the reader needs.');
            });
            loadDownloadPolicy();
            return;
        }

        loadDownloadPolicy();
        readerOwn = { id: String(card.getAttribute('data-id') || '').replace(/-/g, '').toLowerCase(), at: Date.now() };
        wrapReaderPlugins();
        if (reader) {
            reader.teardown();
        }

        watchReader();
    }, true);


    // The scroller measures its slider when it is built and when its frame resizes, not when tiles are added or removed. So after the tiles changed,
    // reload() takes the new size and the scroller's own scroll event lets the buttons update. The measure runs again a little later (a timer, not a
    // frame: a hidden pane gets no frames).
    // A divider (pbConnGroup) is as high as the PICTURE of the tile after it, not the whole card, so it does not make the row taller; the size comes
    // from the picture's real box and is set again when tiles are added or the window changes.
    function sizeDividers(root) {
        [].forEach.call(root && root.querySelectorAll ? root.querySelectorAll('.pbConnGroup') : [], function (divider) {
            var card = divider.nextElementSibling;
            while (card && !card.classList.contains('card')) {
                card = card.nextElementSibling;
            }

            var picture = card && card.querySelector('.cardScalable');
            if (!picture || !picture.offsetHeight) {
                return;
            }

            divider.style.alignSelf = 'flex-start';
            divider.style.marginTop = '0px';
            divider.style.height = picture.getBoundingClientRect().height + 'px';
            var shift = picture.getBoundingClientRect().top - divider.getBoundingClientRect().top;
            if (Math.abs(shift) > 0.5) {
                divider.style.marginTop = shift + 'px';
            }

            if (!picture.pbDivider && window.ResizeObserver) {
                picture.pbDivider = new window.ResizeObserver(function () { sizeDividers(divider.parentNode); });
                picture.pbDivider.observe(picture);
            }
        });
    }

    var resizeTimer = 0;
    window.addEventListener('resize', function () {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(function () {
            var done = [];
            [].forEach.call(document.querySelectorAll('.pbConnGroup'), function (divider) {
                if (done.indexOf(divider.parentNode) < 0) {
                    done.push(divider.parentNode);
                    sizeDividers(divider.parentNode);
                }
            });
        }, 120);
    });

    // The user's title for the local special features on Jellyfin's own row title.
    function applySpecialsTitle(view) {
        var title = labelOptions.specials && view.querySelector('#specialsCollapsible .sectionTitle');
        if (title && title.textContent !== labelOptions.specials) {
            title.textContent = labelOptions.specials;
        }
    }

    function remeasureScroller(row) {
        var measure = function () {
            sizeDividers(row);
            var frame = row && row.querySelector('[is="emby-scroller"]');
            if (frame && frame.scroller && frame.getScrollEventName) {
                frame.scroller.reload();
                frame.dispatchEvent(new CustomEvent(frame.getScrollEventName(), { bubbles: true, cancelable: false }));
            }
        };
        measure();
        setTimeout(measure, 80);
        setTimeout(measure, 600);
    }

    // Opens or closes the tracks beside the album card; a row whose list has not come yet opens when it does (fetchSoundtrack).
    function setSoundtrackOpen(row, open) {
        row.pbOpen = open;
        var container = row.querySelector('.itemsContainer');
        var album = container.querySelector('.pbAlbum');
        album.querySelector('.pbAlbumButton').setAttribute('aria-expanded', open ? 'true' : 'false');
        [].forEach.call(container.querySelectorAll('.pbTrack'), function (card) { card.parentNode.removeChild(card); });
        if (open && row.pbTracks) {
            bindSoundtrack(row);
            album.insertAdjacentHTML('afterend', soundtrackCards(row.pbTracks, true));
            fillImages(container);
        }
        remeasureScroller(row);
    }

    function fetchSoundtrack(row) {
        var api = window.ApiClient;
        var version = row.pbVersion;
        row.pbLoading = version;
        api.getJSON(api.getUrl('PlayniteBridge/games/' + encodeURIComponent(version) + '/soundtrack')).then(function (tracks) {
            if (row.pbVersion !== version || !row.parentNode) {
                return;
            }
            row.pbLoading = null;
            row.pbLoaded = version;
            row.pbTracks = Array.isArray(tracks) ? tracks : [];
            if (!row.pbTracks.length) {
                row.parentNode.removeChild(row);
                return;
            }
            row.pbTracks.forEach(function (item) {
                soundtrackIds[item.Id.replace(/-/g, '').toLowerCase()] = true;
                extraIds[item.Id] = version.replace(/-/g, '').toLowerCase();
            });
            bindSoundtrack(row);
            var container = row.querySelector('.itemsContainer');
            container.querySelector('.pbAlbum').outerHTML = albumCardHtml(row);
            fillImages(container);
            if (row.pbOpen) {
                setSoundtrackOpen(row, true);
            } else {
                remeasureScroller(row);
            }
        }, function () {
            row.pbLoading = null;
            row.pbFailed = version;
        });
    }

    function loadSoundtrack(view, request, game, dictionary, order) {
        var id = view.pbVersion;
        var shown = (game.versions || []).filter(function (v) { return v && typeof v.id === 'string' && v.id.replace(/-/g, '').toLowerCase() === id; })[0];
        var count = shown && shown.soundtrack > 0 ? shown.soundtrack : 0;
        var row = view.querySelector('#pbSoundtrackCollapsible');
        if (!count) {
            if (row) {
                row.parentNode.removeChild(row);
            }
            return;
        }
        var name = labelOptions.soundtrack || translate(dictionary, 'Soundtrack') || 'Soundtrack';
        if (!row) {
            var holder = document.createElement('div');
            holder.innerHTML = soundtrackRowHtml();
            row = holder.firstChild;
            row.pbName = name;
            row.querySelector('.sectionTitle').textContent = name;
            row.querySelector('.itemsContainer').innerHTML = albumCardHtml(row);
            row.addEventListener('click', function (e) {
                var album = e.target.closest && e.target.closest('.pbAlbum');
                if (album) {
                    e.preventDefault();
                    e.stopPropagation();
                    if (!row.pbLoaded && !row.pbLoading) {
                        fetchSoundtrack(row);
                        return;
                    }

                    setSoundtrackOpen(row, !row.pbOpen);
                    return;
                }
                bindSoundtrack(row);
            }, true);
            if (!insertRow(view, order, 'Soundtrack', row)) {
                return;
            }
            row.pbVersion = id;
            fetchSoundtrack(row);
            return;
        }
        row.pbName = name;
        row.querySelector('.sectionTitle').textContent = name;
        if (row.pbVersion !== id) {
            row.pbVersion = id;
            row.pbTracks = null;
            row.pbLoaded = null;
            var container = row.querySelector('.itemsContainer');
            [].forEach.call(container.querySelectorAll('.pbTrack'), function (card) { card.parentNode.removeChild(card); });
            container.querySelector('.pbAlbum').outerHTML = albumCardHtml(row);
            fetchSoundtrack(row);
        }
    }

    // Screenshots: the game's own screenshots (games/{id}/screenshots) as rows of picture tiles in one wrapper, #pbScreenshotsCollapsible, placed by
    // RowOrder like the other rows. A tile's click plays a Photo item, which opens Jellyfin's own viewer; closing it fires popstate only, which a
    // game page ignores.
    // The first 60 tiles of a row come with the list, a "Show more" card asks for the next 60. The wrapper keeps the height of the highest version's
    // rows, so a version switch does not move what lies below.
    // Screenshots and Wallpapers & Artwork run on one engine (pictureCards, pictureRow, showMorePictures, loadPictureArea) and differ only in their
    // spec; a row's tiles have the shape the server sent, and Picture Fit decides how each picture fills the tile.
    var shotLabels = { screenshots: 'Screenshots', title: 'ScreenshotTitle', select: 'ScreenshotSelect', gameplay: 'ScreenshotGameplay', gameover: 'ScreenshotGameOver', highscores: 'ScreenshotHighScores',
        titlescreenshots: 'ScreenshotTitleScreenshots', selectscreenshots: 'ScreenshotSelectScreenshots', gameplayscreenshots: 'ScreenshotGameplayScreenshots',
        gameoverscreenshots: 'ScreenshotGameOverScreenshots', highscoresscreenshots: 'ScreenshotHighScoresScreenshots' };
    var wpPresets = { wallpapersmerged: 'WallpapersAndArtwork', wallpapers: 'Wallpapers', fanart: 'FanArt', conceptart: 'ConceptArt', posters: 'Posters', boxart: 'BoxArt', renders: 'Renders', logos: 'Logos', icons: 'Icons' };
    // spec: name (area), wrapId, endpoint, countsKey (versions[].<key> = the tile count of each row), rowToken (RowOrder), mergedKey (the row key of the merged or flat row, which the admin
    // may have named: labelOption), table (preset key -> translation key), titleOption / subtextOption (textOptions)
    var PICTURE_AREAS = {
        screenshots: { name: 'screenshots', wrapId: 'pbScreenshotsCollapsible', endpoint: 'screenshots', countsKey: 'screenshots', rowToken: 'Screenshots', mergedKey: 'screenshots', labelOption: 'screenshots',
            table: shotLabels, titleOption: 'shotTitle', subtextOption: 'shotSubtext' },
        wallpapers: { name: 'wallpapers', wrapId: 'pbWallpapersCollapsible', endpoint: 'wallpapers', countsKey: 'wallpapers', rowToken: 'Wallpapers', mergedKey: 'wallpapersmerged', labelOption: 'wallpapers',
            table: wpPresets, titleOption: 'wpTitle', subtextOption: 'wpSubtext' }
    };

    // A predefined label in the UI language (the table), the admin's title of the merged or flat row, an own label or a missing translation as the server sent it.
    function pictureLabel(spec, dictionary, row) {
        if (row.key === spec.mergedKey && labelOptions[spec.labelOption]) {
            return labelOptions[spec.labelOption];
        }

        return row.key && spec.table[row.key] && translate(dictionary, spec.table[row.key]) || row.label;
    }

    // The three tile shapes of a picture row: the card class and the padder class of Jellyfin's cards (overflowBackdrop = 16:9, overflowSquare, overflowPortrait = 2:3).
    var TILE_SHAPES = { landscape: ['overflowBackdropCard', 'cardPadder-overflowBackdrop'], square: ['overflowSquareCard', 'cardPadder-overflowSquare'], portrait: ['overflowPortraitCard', 'cardPadder-overflowPortrait'] };

    function tileShape(shape) {
        return TILE_SHAPES[shape] ? shape : 'landscape';
    }

    // The dashed "Show more" card at the end of a row, as big as a tile of the row (cardClass, padderClass).
    function moreCard(dictionary, cardClass, padderClass) {
        var tv = document.documentElement.classList.contains('layout-tv');
        return '<div class="card ' + cardClass + ' pbShotMore"><div class="cardBox cardBox-bottompadded"><div class="cardScalable"><div class="cardPadder ' + padderClass + '"></div>'
            + '<button type="button" class="pbShowMoreBtn' + (tv ? ' show-focus' : '') + '">' + escapeHtml(translate(dictionary, 'ShowMore') || 'Show more') + '</button></div></div></div>';
    }

    function pictureMoreCard(dictionary, shape) {
        var classes = TILE_SHAPES[tileShape(shape)];
        return moreCard(dictionary, classes[0], classes[1]);
    }

    // The divider of the shared Connections row, also between the slots of the merged screenshots row and between the local and the Steam special features.
    function groupDivider(label, extra) {
        return '<div class="pbConnGroup ' + extra + '" role="presentation"><span title="' + escapeHtml(label) + '">' + escapeHtml(label) + '</span></div>';
    }

    // Show Dividers of the merged row: the answer of games/{id}/screenshots names the slots (slotNames) only for a merged row with its slots together and more than one slot; previous =
    // the slot of the tile before the first one given (null at the start of the row).
    function shotDivide(row, dictionary, previous) {
        return row.slotNames ? { names: row.slotNames, dictionary: dictionary, previous: previous } : null;
    }

    // The tiles of items [{ id, tag, slot }]; offset = how many tiles the row has already (for the accessible names); divide: see shotDivide; shape: the row's tile shape.
    function pictureCards(spec, items, label, offset, divide, names, dictionary, shape) {
        var classes = TILE_SHAPES[tileShape(shape)];
        var layout = document.documentElement.classList;
        var tv = layout.contains('layout-tv');
        var api = window.ApiClient;
        var previous = divide ? divide.previous : null;
        return items.map(function (item, index) {
            var head = '';
            if (divide && item.slot !== previous && divide.names[item.slot]) {
                head = groupDivider(pictureLabel(spec, divide.dictionary, divide.names[item.slot]), 'pbShotGroup');
            }
            previous = item.slot;
            var name = escapeHtml(label) + ' ' + (offset + index + 1);
            var url = api.getScaledImageUrl(item.id, { type: 'Primary', maxWidth: 480, quality: 96, tag: item.tag });
            var image = '<div class="cardImageContainer coveredImage cardContent lazy' + (fitBlur(spec.name, item.id + ':' + item.tag, SHAPE_RATIOS[tileShape(shape)]) ? ' pbFitBlur' : '') + (tv ? '' : ' itemAction pbShot') + '"'
                + (tv ? '' : ' data-action="play" role="button" tabindex="0" aria-label="' + name + '"') + ' data-src="' + url + '"></div>';
            return head + '<' + (tv ? 'button type="button"' : 'div') + ' data-index="' + (offset + index) + '" data-pb-slot="' + item.slot + '"' + (tv ? ' data-action="play" aria-label="' + name + '"' : '')
                + ' data-isfolder="false" data-serverid="' + api.serverId() + '" data-id="' + item.id + '" data-type="Photo" data-mediatype="Photo" data-tag="' + escapeHtml(item.tag || '') + '" class="card ' + classes[0]
                + (tv ? ' show-focus show-animation itemAction pbShot' : '') + '"><div class="cardBox cardBox-bottompadded"><div class="cardScalable"><div class="cardPadder ' + classes[1] + '"></div>'
                + image + '</div>' + tileTextLines(textOptions[spec.titleOption], textOptions[spec.subtextOption], item.name, names && names[item.slot] ? pictureLabel(spec, dictionary || {}, names[item.slot]) : '', 'play')
                + '</div></' + (tv ? 'button' : 'div') + '>';
        }).join('');
    }

    // One row's section with an empty tile container (the tiles come in the same task, see loadPictureArea).
    function pictureRow(spec, row, dictionary) {
        return '<div class="verticalSection detailVerticalSection pbShotRow" data-pb-row="' + row.row + '" data-pb-count="' + row.count + '" data-pb-shape="' + tileShape(row.shape) + '">'
            + '<h2 class="sectionTitle sectionTitle-cards padded-right">' + escapeHtml(pictureLabel(spec, dictionary, row)) + '</h2>'
            + '<div is="emby-scroller" class="padded-top-focusscale padded-bottom-focusscale no-padding" data-centerfocus="true">'
            + '<div is="emby-itemscontainer" class="scrollSlider focuscontainer-x itemsContainer"></div></div></div>';
    }

    // The next 60 tiles of a row (the button's row), for the screenshots, the wallpapers and the documents: appended in place, the card moves to the end or goes when the row is complete.
    // spec = { endpoint, rowClass, cardSelector, build(row, label, loaded, last, dictionary), more(dictionary, row) }.
    function showMoreTiles(wrap, button, spec) {
        var api = window.ApiClient;
        var section = button.closest(spec.rowClass);
        var container = section && section.querySelector('.itemsContainer');
        var key = wrap.pbKey;
        if (!container || button.disabled) {
            return;
        }
        var loaded = container.querySelectorAll(spec.cardSelector).length;
        button.disabled = true;
        api.getJSON(api.getUrl('PlayniteBridge/games/' + encodeURIComponent(wrap.pbId) + '/' + spec.endpoint, {
            row: section.getAttribute('data-pb-row'), from: loaded, seed: wrap.pbSeed || 0
        })).then(function (answer) {
            var row = answer && answer.rows && answer.rows[0];
            if (wrap.pbKey !== key || !section.isConnected || !row) {
                button.disabled = false;
                return;
            }
            var label = section.querySelector('.sectionTitle').textContent;
            var more = container.querySelector('.pbShotMore');
            if (more) {
                more.parentNode.removeChild(more);
            }
            var last = [].slice.call(container.querySelectorAll('.card[data-pb-slot]')).pop();
            container.insertAdjacentHTML('beforeend', spec.build(row, label, loaded, last ? Number(last.getAttribute('data-pb-slot')) : null, wrap.pbDictionary || {})
                + (loaded + row.items.length < row.count ? spec.more(wrap.pbDictionary || {}, row) : ''));
            fillImages(section);
            if (spec.after) {
                spec.after();
            }
        }, function () {
            button.disabled = false;
        });
    }

    function showMorePictures(spec, wrap, button) {
        showMoreTiles(wrap, button, {
            endpoint: spec.endpoint, rowClass: '.pbShotRow', cardSelector: '.card[data-mediatype="Photo"]',
            build: function (row, label, loaded, last, dictionary) { return pictureCards(spec, row.items, label, loaded, shotDivide(row, dictionary, last), row.names, dictionary, row.shape); },
            more: function (dictionary, row) { return pictureMoreCard(dictionary, row.shape); },
            after: function () { fitTiles(wrap, spec.name); }
        });
    }

    // The area for the version shown on the view (view.pbVersion). game.versions[].<countsKey> = the tile count of each row of that version; no
    // wrapper when no version has pictures.
    // The height one tile row had when last drawn, per area: kept while the rows' answer is on its way, so what lies below does not jump.
    var pictureRowHeights = { screenshots: 240, wallpapers: 240 };

    function loadPictureArea(spec, view, request, game, dictionary, order) {
        var api = window.ApiClient;
        var id = view.pbVersion;
        var versions = (game.versions || []).filter(function (v) { return v && typeof v.id === 'string'; });
        var rowsOf = function (v) { return v && Array.isArray(v[spec.countsKey]) ? v[spec.countsKey] : []; };
        var most = versions.reduce(function (m, v) { return Math.max(m, rowsOf(v).length); }, 0);
        var shown = versions.filter(function (v) { return v.id.replace(/-/g, '').toLowerCase() === id; })[0];
        var counts = rowsOf(shown);
        var wrap = view.querySelector('#' + spec.wrapId);
        if (!most) {
            if (wrap) {
                wrap.parentNode.removeChild(wrap);
            }
            return;
        }
        if (!wrap) {
            wrap = document.createElement('div');
            wrap.id = spec.wrapId;
            wrap.addEventListener('click', function (e) {
                var button = e.target.closest && e.target.closest('.pbShowMoreBtn');
                if (button) {
                    showMorePictures(spec, wrap, button);
                }
            });
            wrap.addEventListener('keydown', function (e) {
                if ((e.key === 'Enter' || e.key === ' ') && e.target.classList && e.target.classList.contains('pbShot') && e.target.tagName !== 'BUTTON') {
                    e.preventDefault();
                    e.target.click();
                }
            });
            if (!insertRow(view, order, spec.rowToken, wrap)) {
                return;
            }
        }
        wrap.pbDictionary = dictionary;
        var key = id + ':' + counts.join(',');
        if (wrap.pbKey === key) {
            return;
        }
        wrap.pbKey = key;
        if (!counts.length) {
            wrap.pbId = null;
            // a version without rows keeps the room of the rows it replaces while the switch runs (what lies below does not move under the hold); the room goes when it has settled
            wrap.style.minHeight = wrap.offsetHeight + 'px';
            wrap.innerHTML = '';
            keepRoom(view, wrap);
            return;
        }
        if (!wrap.firstChild) {
            wrap.style.minHeight = Math.round(counts.length * pictureRowHeights[spec.name]) + 'px';
        }
        api.getJSON(api.getUrl('PlayniteBridge/games/' + encodeURIComponent(id) + '/' + spec.endpoint)).then(function (answer) {
            if (view.pbRequest !== request || wrap.pbKey !== key) {
                return;
            }
            var rows = answer && answer.rows || [];
            wrap.pbId = id;
            wrap.pbSeed = answer && answer.seed || 0;
            var holder = document.createElement('div');
            holder.innerHTML = rows.map(function (row) { return pictureRow(spec, row, dictionary); }).join('');
            wrap.style.minHeight = '';
            wrap.innerHTML = '';
            while (holder.firstChild) {
                wrap.appendChild(holder.firstChild);
            }
            // The tiles at once, so the row has its height from the first frame; the scroller is measured again in the next task.
            [].forEach.call(wrap.querySelectorAll('.pbShotRow'), function (section, index) {
                var row = rows[index];
                var label = pictureLabel(spec, dictionary, row);
                section.querySelector('.itemsContainer').innerHTML = pictureCards(spec, row.items, label, 0, shotDivide(row, dictionary, null), row.names, dictionary, row.shape)
                    + (row.items.length < row.count ? pictureMoreCard(dictionary, row.shape) : '');
            });
            if (rows.length && wrap.offsetHeight > 0) {
                pictureRowHeights[spec.name] = wrap.offsetHeight / rows.length;
            }
            fitTiles(wrap, spec.name);
            setTimeout(function () {
                if (wrap.pbKey !== key) {
                    return;
                }
                [].forEach.call(wrap.querySelectorAll('.pbShotRow'), function (section) {
                    fillImages(section);
                    remeasureScroller(section);
                });
            });
        }, function () {
            if (wrap.pbKey === key) {
                wrap.pbKey = null;
            }
        });
    }

    function loadScreenshots(view, request, game, dictionary, order) {
        loadPictureArea(PICTURE_AREAS.screenshots, view, request, game, dictionary, order);
    }

    function loadWallpapers(view, request, game, dictionary, order) {
        loadPictureArea(PICTURE_AREAS.wallpapers, view, request, game, dictionary, order);
    }


    // Documents: the game's own documents (games/{id}/documents; PDF, EPUB, CBZ, CBR) as rows of picture tiles in one wrapper,
    // #pbDocumentsCollapsible, placed by RowOrder like the screenshots. A click opens Jellyfin's own reader for the file type (chosen by the item's
    // Path, carried as data-path).
    var docLabels = { documents: 'Documents', manuals: 'DocumentManuals', guides: 'DocumentGuides', maps: 'DocumentMaps', referencecards: 'DocumentReferenceCards', books: 'Books',
        artbooks: 'DocumentArtBooks', comics: 'DocumentComics', magazines: 'DocumentMagazines', articles: 'DocumentArticles', booklets: 'DocumentBooklets', merchandise: 'DocumentMerchandise' };

    function docLabel(dictionary, row) {
        if (row.key === 'documents' && labelOptions.documents) {
            return labelOptions.documents;
        }

        return row.key && docLabels[row.key] && translate(dictionary, docLabels[row.key]) || row.label;
    }

    // Page-1 snapshots: the tile of a PDF shows page 1, drawn in the browser by Jellyfin's own pdf.js (found among webpack's chunks at run time); the
    // server's picture stays until the snapshot is ready and on any failure. Only tiles on screen are drawn (IntersectionObserver, two at a time). A
    // snapshot is kept per picture tag (an empty entry = failed).
    var pdfSnapshots = {};
    // Picture Fit Adaptive (Documents): the width / height of a tile's picture per item id + tag (the server's picture, and the page-1 snapshot of a PDF, which wins once it is there).
    var fitRatios = {};
    var snapRatios = {};
    var pdfLibrary = null;
    var pdfQueue = [];
    var pdfBusy = 0;

    function loadPdfLibrary() {
        if (!pdfLibrary) {
            pdfLibrary = Promise.resolve().then(function () {
                var webpack = null;
                window.webpackChunk.push([[Symbol('pb')], {}, function (require) { webpack = require; }]);
                var chunk = /(\d+):"node_modules\.pdfjs-dist"/.exec(webpack.u.toString());
                return webpack.e(Number(chunk[1])).then(function () {
                    var best = null;
                    var length = 0;
                    Object.keys(webpack.m).forEach(function (id) {
                        var source = Function.prototype.toString.call(webpack.m[id]);
                        if (source.length > length && source.indexOf('GlobalWorkerOptions') >= 0 && source.indexOf('getDocument') >= 0) {
                            best = id;
                            length = source.length;
                        }
                    });
                    var pdfjs = webpack(best);
                    if (!pdfjs.GlobalWorkerOptions.workerSrc) {
                        pdfjs.GlobalWorkerOptions.workerSrc = new URL('libraries/pdf.worker.js', document.baseURI).href;
                    }
                    return pdfjs;
                });
            });
        }

        return pdfLibrary;
    }

    // Page 1 of the file at url as a JPEG data URL about 400 px wide; rejects on any failure and gives up after a minute.
    function snapshotPdf(url) {
        return loadPdfLibrary().then(function (pdfjs) {
            var task = pdfjs.getDocument({ url: url, isEvalSupported: false, disableAutoFetch: true, disableStream: true });
            var timer = null;
            var drawing = new Promise(function (resolve, reject) {
                timer = setTimeout(function () { reject(new Error('timeout')); }, 60000);
                task.promise.then(function (pdf) {
                    return pdf.getPage(1).then(function (page) {
                        var viewport = page.getViewport({ scale: 400 / page.getViewport({ scale: 1 }).width });
                        var canvas = document.createElement('canvas');
                        canvas.width = Math.round(viewport.width);
                        canvas.height = Math.round(viewport.height);
                        var context = canvas.getContext('2d');
                        context.fillStyle = '#fff';
                        context.fillRect(0, 0, canvas.width, canvas.height);
                        return page.render({ canvasContext: context, canvas: canvas, viewport: viewport }).promise.then(function () {
                            resolve({ data: canvas.toDataURL('image/jpeg', 0.85), ratio: canvas.width / canvas.height });
                        });
                    });
                }).catch(reject);
            });
            var done = function () {
                clearTimeout(timer);
                task.destroy();
            };
            drawing.then(done, done);
            return drawing;
        });
    }

    // CBR comics (RAR archives): page 1 is drawn by Jellyfin's comic reader library, libarchive.js (a webpack chunk found at run time like pdf.js).
    // Only the first image is unpacked; the file (150 MB at most) is read as a whole.
    var archiveLibrary = null;
    var COMIC_IMAGES = ['jpg', 'jpeg', 'jpe', 'jif', 'jfif', 'jfi', 'png', 'avif', 'gif', 'bmp', 'dib', 'tiff', 'tif', 'webp'];

    // The libarchive class (an export with init and open) of the modules that name both workerUrl and getFilesArray, tried in order of likelihood; a
    // module that throws or exports no such class is skipped; null = none.
    function pickArchiveClass(webpack) {
        var candidates = [];
        Object.keys(webpack.m).forEach(function (id) {
            var source = Function.prototype.toString.call(webpack.m[id]);
            if (source.indexOf('workerUrl') >= 0 && source.indexOf('getFilesArray') >= 0) {
                candidates.push({ id: id, length: source.length, reader: /comicsplayer/i.test(source) });
            }
        });
        candidates.sort(function (x, y) { return (x.reader ? 1 : 0) - (y.reader ? 1 : 0) || y.length - x.length; });
        for (var i = 0; i < candidates.length; i++) {
            try {
                var exported = webpack(candidates[i].id);
                var Archive = exported && Object.keys(exported).map(function (name) { return exported[name]; }).filter(function (value) {
                    return value && typeof value.init === 'function' && typeof value.open === 'function';
                })[0];
                if (Archive) {
                    return Archive;
                }
            } catch (e) {
            }
        }

        return null;
    }

    function loadArchiveLibrary() {
        if (!archiveLibrary) {
            archiveLibrary = Promise.resolve().then(function () {
                var webpack = null;
                window.webpackChunk.push([[Symbol('pb')], {}, function (require) { webpack = require; }]);
                // a separate library chunk exists in 10.10.7; where it is bundled into the reader's chunk (Jellyfin 12) the modules that are loaded are searched as they are
                var chunk = /(\d+):"node_modules\.libarchive\.js"/.exec(webpack.u.toString());
                return (chunk ? webpack.e(Number(chunk[1])) : Promise.resolve()).then(function () {
                    var Archive = pickArchiveClass(webpack);
                    if (!Archive) {
                        throw new Error('no archive library');
                    }

                    Archive.init({ workerUrl: new URL('libraries/worker-bundle.js', document.baseURI).href });
                    return Archive;
                });
            });
        }

        return archiveLibrary;
    }

    // Page 1 of the comic at url (the first image of the archive by name, as the reader orders them) as a JPEG data URL about 400 px wide; rejects on any failure, gives up after two minutes.
    function snapshotComic(url) {
        var archive = null;
        var timer = null;
        var drawing = new Promise(function (resolve, reject) {
            timer = setTimeout(function () { reject(new Error('timeout')); }, 120000);
            Promise.all([loadArchiveLibrary(), fetch(url).then(function (response) {
                if (!response.ok) {
                    var failure = new Error('download');
                    failure.status = response.status;
                    throw failure;
                }
                return response.blob();
            })]).then(function (both) {
                return both[0].open(both[1]);
            }).then(function (opened) {
                archive = opened;
                return archive.getFilesArray();
            }).then(function (entries) {
                var pages = entries.filter(function (entry) {
                    var name = entry.file.name || '';
                    return name.lastIndexOf('.') > 0 && COMIC_IMAGES.indexOf(name.slice(name.lastIndexOf('.') + 1).toLowerCase()) >= 0;
                }).sort(function (a, b) { return a.file.name < b.file.name ? -1 : 1; });
                if (!pages.length) {
                    throw new Error('no page');
                }
                return pages[0].file.extract ? pages[0].file.extract() : pages[0].file;
            }).then(function (file) {
                return createImageBitmap(file);
            }).then(function (bitmap) {
                var canvas = document.createElement('canvas');
                canvas.width = 400;
                canvas.height = Math.max(1, Math.round(bitmap.height * 400 / bitmap.width));
                var context = canvas.getContext('2d');
                context.fillStyle = '#fff';
                context.fillRect(0, 0, canvas.width, canvas.height);
                context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
                bitmap.close();
                resolve({ data: canvas.toDataURL('image/jpeg', 0.85), ratio: canvas.width / canvas.height });
            }).catch(reject);
        });
        var done = function () {
            clearTimeout(timer);
            if (archive && archive.close) {
                archive.close();
            }
        };
        drawing.then(done, done);
        return drawing;
    }

    // Generate Thumbnails: a page-1 snapshot is posted once per document and page load (the server keeps the first one and refuses a second with 409; any other answer is not tried again
    // either). fetch, not ApiClient.ajax: the body is the JPEG itself. Never a message to the user; true when the server stored it.
    var thumbPosted = {};

    function authFetch(path, options) {
        var api = window.ApiClient;
        options = options || {};
        options.headers = Object.assign({ Authorization: 'MediaBrowser Token="' + api.accessToken() + '"' }, options.headers || {});
        return fetch(api.getUrl(path), options);
    }

    function postThumb(id, dataUrl) {
        if (!textOptions.docThumbs || downloadAllowed === false || thumbPosted[id] || !window.ApiClient || typeof dataUrl !== 'string' || dataUrl.indexOf('data:image/jpeg;base64,') !== 0) {
            return Promise.resolve(false);
        }
        thumbPosted[id] = true;
        var bytes;
        try {
            var raw = atob(dataUrl.slice(dataUrl.indexOf(',') + 1));
            bytes = new Uint8Array(raw.length);
            for (var i = 0; i < raw.length; i++) {
                bytes[i] = raw.charCodeAt(i);
            }
        } catch (e) {
            return Promise.resolve(false);
        }
        return authFetch('PlayniteBridge/documents/' + id + '/thumbnail', { method: 'POST', headers: { 'Content-Type': 'image/jpeg' }, body: bytes }).then(function (response) {
            if (response.status === 400) {
                reportThumbFailure(id);
            }
            return response.status === 201;
        }, function () { return false; });
    }

    // A document a browser could not draw is reported once per page load; the server keeps it off the pending list for 7 days. Silent, no retry.
    var thumbFailed = {};

    function isAuthFailure(error) {
        return !!error && (error.status === 401 || error.status === 403);
    }

    function reportThumbFailure(id) {
        if (!textOptions.docThumbs || downloadAllowed === false || thumbFailed[id] || !window.ApiClient) {
            return;
        }
        thumbFailed[id] = true;
        authFetch('PlayniteBridge/documents/' + id + '/thumbnail-failed', { method: 'POST' }).catch(function () { });
    }

    // The snapshot function of a document by its file name: PDF has one, a CBR too unless the server reads it itself (serverComics, the Jellyfin 12.x build: SharpCompress), nothing else.
    function snapshotKind(path) {
        return /\.pdf$/i.test(path) ? snapshotPdf : /\.cbr$/i.test(path) && !textOptions.serverComics ? snapshotComic : null;
    }

    // Pending thumbnails: the browser of an administrator with the download right works off the list Sync Extras made (GET documents/pending, 25 at a
    // time), one document at a time (two while the page is untouched for 10 s, one for a file over 50 MB), draws page 1 as a tile does and posts it.
    // Started once per page load, 10 s after the script. Nothing starts while the tab is hidden or the user was active in the last 3 s; the list is
    // asked again 60 s after a batch and 10 min after an empty answer. A document that cannot be drawn is remembered for the page's life and reported
    // (reportThumbFailure).
    var thumbWorkerStarted = false;
    var pendingFailed = {};

    function startThumbWorker() {
        if (thumbWorkerStarted || !window.ApiClient) {
            return;
        }
        thumbWorkerStarted = true;
        var worker = { lastActivity: Date.now(), busy: 0, big: false, timer: null, queue: [], stopped: false, asking: false };
        var events = ['pointermove', 'keydown', 'wheel', 'touchstart'];
        var touched = function () { worker.lastActivity = Date.now(); };
        var after = function (ms, then) {
            clearTimeout(worker.timer);
            worker.timer = setTimeout(then || pump, ms);
        };
        var stop = function () {
            worker.stopped = true;
            clearTimeout(worker.timer);
            events.forEach(function (name) { document.removeEventListener(name, touched, true); });
            document.removeEventListener('visibilitychange', wake);
            window.removeEventListener('pagehide', stop);
        };
        var ask = function () {
            worker.asking = true;
            authFetch('PlayniteBridge/documents/pending').then(function (response) {
                if (response.status === 200) {
                    return response.json();
                }
                if (response.status !== 204) {
                    stop();
                }
                return null;
            }).then(function (answer) {
                worker.asking = false;
                if (worker.stopped) {
                    return;
                }
                worker.queue = answer && answer.items ? answer.items.filter(function (entry) { return !pendingFailed[entry.id] && !thumbPosted[entry.id]; }) : [];
                // nothing to do (or only documents that failed here): ask again in 10 minutes
                if (worker.queue.length) {
                    pump();
                } else {
                    after(600000, ask);
                }
            }, function () {
                worker.asking = false;
                after(600000, ask);
            });
        };
        var next = function () {
            var entry = worker.queue.shift();
            var api = window.ApiClient;
            var url = tokenUrl(api, 'Items/' + entry.id + '/Download');
            worker.busy++;
            var big = entry.size > 50000000;
            worker.big = big;
            (entry.kind === 'comic' ? snapshotComic : snapshotPdf)(url).then(function (shot) {
                return postThumb(entry.id, shot.data);
            }, function (error) {
                // a refused request (401, 403: login, download right) says nothing about the document: no marker, tried again on the next page load
                if (isAuthFailure(error)) {
                    return;
                }
                pendingFailed[entry.id] = true;
                reportThumbFailure(entry.id);
            }).then(function () {
                worker.busy--;
                if (big) {
                    worker.big = false;
                }
                pump();
            });
        };
        function pump() {
            if (worker.stopped || worker.asking) {
                return;
            }
            if (!worker.queue.length) {
                // a batch is worked off: the next one after a minute (an empty answer waits 10 minutes, see ask)
                if (!worker.busy) {
                    after(60000, ask);
                }
                return;
            }
            // a hidden tab waits for visibilitychange, a page the user operates for 3 seconds without a touch
            if (document.hidden) {
                return;
            }
            var idle = Date.now() - worker.lastActivity;
            if (idle < 3000) {
                after(3000 - idle + 50);
                return;
            }
            var lanes = idle > 10000 ? 2 : 1;
            while (worker.busy < lanes && worker.queue.length) {
                if (worker.big || (worker.queue[0].size > 50000000 && worker.busy > 0)) {
                    break;
                }
                next();
            }
        }
        function wake() {
            if (worker.queue.length) {
                pump();
            }
        }
        events.forEach(function (name) { document.addEventListener(name, touched, { passive: true, capture: true }); });
        document.addEventListener('visibilitychange', wake);
        window.addEventListener('pagehide', stop);
        ask();
    }

    // Ten seconds after the script, once the signed-in user is known (retried every 30 seconds, at most 20 times): an administrator who may download and has Generate Thumbnails on starts the worker.
    function maybeStartThumbWorker(tries) {
        var api = window.ApiClient;
        if (!api || !api.getCurrentUserId || !api.getCurrentUserId()) {
            if (tries < 20) {
                setTimeout(function () { maybeStartThumbWorker(tries + 1); }, 30000);
            }
            return;
        }
        api.getCurrentUser().then(function (user) {
            var policy = user && user.Policy || {};
            if (policy.IsAdministrator && policy.EnableContentDownloading !== false && textOptions.docThumbs) {
                startThumbWorker();
            }
        }, function () { });
    }

    setTimeout(function () { maybeStartThumbWorker(0); }, 10000);

    function showSnapshot(tile, data) {
        var picture = tile.querySelector('.cardImageContainer');
        if (picture && data) {
            picture.pbSnapshot = true;
            setFit(picture, snapRatios[tile.getAttribute('data-id') + ':' + tile.getAttribute('data-tag')], 'documents', DOC_TILE_RATIO);
            picture.style.backgroundImage = "url('" + data + "')";
            picture.classList.remove('lazy-hidden');
            picture.classList.add('lazy-image-fadein-fast');
        }
    }

    // One tile's snapshot (a function of its own: two tiles can start in one call of the pump, and each callback must see its own tile, id and key).
    function drawPdfTile(tile) {
        var api = window.ApiClient;
        var id = tile.getAttribute('data-id');
        var key = id + ':' + tile.getAttribute('data-tag');
        var snapshot = snapshotKind(tile.getAttribute('data-path') || '');
        if (!snapshot) {
            return;
        }

        pdfBusy++;
        snapshot(tokenUrl(api, 'Items/' + id + '/Download')).then(function (shot) {
            snapRatios[key] = shot.ratio;
            if (!tile.hasAttribute('data-thumb')) {
                postThumb(id, shot.data);
            }
            return shot.data;
        }, function (error) {
            // not drawable here (password, damaged): the server keeps it off the pending list for a week; a refused request (401, 403) is not the document's fault
            if (!tile.hasAttribute('data-thumb') && !isAuthFailure(error)) {
                reportThumbFailure(id);
            }
            return '';
        }).then(function (data) {
            pdfSnapshots[key] = data;
            pdfBusy--;
            showSnapshot(tile, data);
            pumpPdfQueue();
        });
    }

    function pumpPdfQueue() {
        while (pdfBusy < 2 && pdfQueue.length) {
            var tile = pdfQueue.shift();
            if (!tile.isConnected || tile.pbView.pbLeft) {
                tile.pbPdf = false;
                continue;
            }
            drawPdfTile(tile);
        }
    }

    // The PDF tiles of the Documents rows are watched: one that comes on screen is drawn. The observer leaves with the view and comes back with a
    // restored viewshow.
    function watchPdfTiles(view) {
        var wrap = view.querySelector('#pbDocumentsCollapsible');
        fitTiles(wrap, 'documents');
        if (view.pbPdfObserver) {
            view.pbPdfObserver.disconnect();
        }
        if (!wrap || !window.IntersectionObserver || downloadAllowed === false) {
            return;
        }
        if (!view.pbPdfObserver) {
            view.pbPdfObserver = new IntersectionObserver(function (entries) {
                entries.forEach(function (entry) {
                    var tile = entry.target;
                    if (!entry.isIntersecting || tile.pbPdf) {
                        return;
                    }
                    view.pbPdfObserver.unobserve(tile);
                    var key = tile.getAttribute('data-id') + ':' + tile.getAttribute('data-tag');
                    if (key in pdfSnapshots) {
                        showSnapshot(tile, pdfSnapshots[key]);
                        return;
                    }
                    tile.pbPdf = true;
                    tile.pbView = view;
                    pdfQueue.push(tile);
                    pumpPdfQueue();
                });
            });
        }
        [].forEach.call(wrap.querySelectorAll('.card[data-mediatype="Book"]'), function (tile) {
            var path = tile.getAttribute('data-path') || '';
            if (snapshotKind(path) && !tile.hasAttribute('data-thumb') && !(/\.cbr$/i.test(path) && !(Number(tile.getAttribute('data-size')) <= 150000000)) && !tile.pbPdf) {
                view.pbPdfObserver.observe(tile);
            }
        });
    }

    // Picture Fit (Documents, Screenshots, Wallpapers & Artwork): is the picture shown on the blurred fill? Blur Fill always; Adaptive when a crop
    // (cover) would cut off more than the area's Crop Limit, so a page of paper stays cropped and a landscape screenshot is shown whole. Unknown
    // size: the crop look.
    var FIT_OPTIONS = { documents: ['docFit', 'docLimit'], screenshots: ['shotFit', 'shotLimit'], wallpapers: ['wpFit', 'wpLimit'] };
    var FIT_TILES = { documents: '.card[data-mediatype="Book"]', screenshots: '.card[data-mediatype="Photo"]', wallpapers: '.card[data-mediatype="Photo"]' };
    var SHAPE_RATIOS = { landscape: 16 / 9, square: 1, portrait: 2 / 3 };
    var DOC_TILE_RATIO = 2 / 3;
    var fitCount = 0;

    function fitMode(area) {
        return { mode: textOptions[FIT_OPTIONS[area][0]], limit: textOptions[FIT_OPTIONS[area][1]] };
    }

    function fitIsBlur(ratio, tile, limit) {
        return (1 - Math.min(ratio, tile) / Math.max(ratio, tile)) * 100 > limit;
    }

    function tileRatioOf(card) {
        var row = card.closest && card.closest('[data-pb-shape]');
        return row && SHAPE_RATIOS[row.getAttribute('data-pb-shape')] || DOC_TILE_RATIO;
    }

    function fitBlur(area, key, tile) {
        var fit = fitMode(area);
        if (fit.mode !== 'Adaptive') {
            return fit.mode === 'BlurFill';
        }
        var ratio = key in snapRatios ? snapRatios[key] : fitRatios[key];
        return !!ratio && fitIsBlur(ratio, tile, fit.limit);
    }

    function setFit(picture, ratio, area, tile) {
        var fit = fitMode(area);
        if (fit.mode === 'Adaptive' && ratio) {
            picture.classList.toggle('pbFitBlur', fitIsBlur(ratio, tile, fit.limit));
        }
    }

    // Adaptive: tiles whose picture size is not known yet load it and take the class when it is there. Sizes are kept per picture tag, emptied when
    // the table passes 5000.
    function fitTiles(wrap, area) {
        if (!wrap || fitMode(area).mode !== 'Adaptive') {
            return;
        }
        [].forEach.call(wrap.querySelectorAll(FIT_TILES[area]), function (tile) {
            var key = tile.getAttribute('data-id') + ':' + tile.getAttribute('data-tag');
            var picture = tile.querySelector('.cardImageContainer');
            var url = picture && picture.getAttribute('data-src');
            if (!url || key in fitRatios || picture.pbFitLoading) {
                return;
            }
            var ratio = tileRatioOf(tile);
            picture.pbFitLoading = true;
            var image = new Image();
            image.onload = function () {
                image.onload = image.onerror = null;
                if (fitCount++ > 5000) {
                    fitRatios = {};
                    fitCount = 0;
                }
                fitRatios[key] = image.naturalWidth / image.naturalHeight;
                if (!picture.pbSnapshot) {
                    setFit(picture, fitRatios[key], area, ratio);
                }
            };
            image.onerror = function () {
                image.onload = image.onerror = null;
            };
            image.src = url;
        });
    }

    function documentCards(items, divide, names, dictionary) {
        var layout = document.documentElement.classList;
        var tv = layout.contains('layout-tv');
        var api = window.ApiClient;
        var previous = divide ? divide.previous : null;
        return items.map(function (item, index) {
            var head = '';
            if (divide && item.slot !== previous && divide.names[item.slot]) {
                head = groupDivider(docLabel(divide.dictionary, divide.names[item.slot]), 'pbShotGroup');
            }
            previous = item.slot;
            var name = escapeHtml(item.name);
            var url = api.getScaledImageUrl(item.id, { type: 'Primary', maxWidth: 400, quality: 96, tag: item.tag });
            var image = '<div class="cardImageContainer coveredImage cardContent lazy' + (fitBlur('documents', item.id + ':' + item.tag, DOC_TILE_RATIO) ? ' pbFitBlur' : '') + (tv ? '' : ' itemAction pbDoc') + '"'
                + (tv ? '' : ' data-action="play" role="button" tabindex="0" aria-label="' + name + '"') + ' data-src="' + url + '"></div>';
            // The text lines as vanilla's cardBuilder writes them: a link with the full text as its title; a click plays (opens the reader) as the tile does.
            var footer = tileTextLines(textOptions.docTitle, textOptions.docSubtext, item.name, names && names[item.slot] ? docLabel(dictionary || {}, names[item.slot]) : '', 'play');
            return head + '<' + (tv ? 'button type="button"' : 'div') + ' data-index="' + index + '" data-pb-slot="' + item.slot + '"' + (tv ? ' data-action="play" aria-label="' + name + '"' : '')
                + ' data-isfolder="false" data-serverid="' + api.serverId() + '" data-id="' + item.id + '" data-type="Book" data-mediatype="Book" data-tag="' + escapeHtml(item.tag || '') + '" data-size="' + (Number(item.size) || 0) + '"' + (/t$/.test(item.tag || '') ? ' data-thumb="1"' : '') + ' data-path="' + escapeHtml(item.path || '')
                + '" class="card overflowPortraitCard' + (layout.contains('layout-desktop') ? ' card-hoverable' : '') + (tv ? ' show-focus show-animation itemAction pbDoc' : '')
                + '"><div class="cardBox cardBox-bottompadded"><div class="cardScalable"><div class="cardPadder cardPadder-overflowPortrait"></div>'
                + image + '</div>' + footer + '</div></' + (tv ? 'button' : 'div') + '>';
        }).join('');
    }

    function docMoreCard(dictionary) {
        return moreCard(dictionary, 'overflowPortraitCard', 'cardPadder-overflowPortrait');
    }

    function showMoreDocuments(view, wrap, button) {
        showMoreTiles(wrap, button, {
            endpoint: 'documents', rowClass: '.pbDocRow', cardSelector: '.card[data-mediatype="Book"]',
            build: function (row, label, loaded, last, dictionary) {
                return documentCards(row.items, row.slotNames ? { names: row.slotNames, dictionary: dictionary, previous: last } : null, row.names, dictionary);
            },
            more: function (dictionary) { return docMoreCard(dictionary); },
            after: function () { watchPdfTiles(view); }
        });
    }

    function documentRow(row, dictionary) {
        return '<div class="verticalSection detailVerticalSection pbDocRow" data-pb-row="' + row.row + '" data-pb-count="' + row.count + '">'
            + '<h2 class="sectionTitle sectionTitle-cards padded-right">' + escapeHtml(docLabel(dictionary, row)) + '</h2>'
            + '<div is="emby-scroller" class="padded-top-focusscale padded-bottom-focusscale no-padding" data-centerfocus="true">'
            + '<div is="emby-itemscontainer" class="scrollSlider focuscontainer-x itemsContainer"></div></div></div>';
    }

    // The area for the version shown on the view, as loadScreenshots: game.versions[].documents = the tile count of each row.
    var docRowHeight = 240;

    function loadDocuments(view, request, game, dictionary, order) {
        var api = window.ApiClient;
        var id = view.pbVersion;
        var versions = (game.versions || []).filter(function (v) { return v && typeof v.id === 'string'; });
        var rowsOf = function (v) { return v && Array.isArray(v.documents) ? v.documents : []; };
        var most = versions.reduce(function (m, v) { return Math.max(m, rowsOf(v).length); }, 0);
        var shown = versions.filter(function (v) { return v.id.replace(/-/g, '').toLowerCase() === id; })[0];
        var counts = rowsOf(shown);
        loadDownloadPolicy();
        var wrap = view.querySelector('#pbDocumentsCollapsible');
        if (!most) {
            if (wrap) {
                wrap.parentNode.removeChild(wrap);
            }
            return;
        }
        if (!wrap) {
            wrap = document.createElement('div');
            wrap.id = 'pbDocumentsCollapsible';
            wrap.addEventListener('click', function (e) {
                var button = e.target.closest && e.target.closest('.pbShowMoreBtn');
                if (button) {
                    showMoreDocuments(view, wrap, button);
                }
            });
            wrap.addEventListener('keydown', function (e) {
                if ((e.key === 'Enter' || e.key === ' ') && e.target.classList && e.target.classList.contains('pbDoc') && e.target.tagName !== 'BUTTON') {
                    e.preventDefault();
                    e.target.click();
                }
            });
            if (!insertRow(view, order, 'Documents', wrap)) {
                return;
            }
        }
        var key = id + ':' + counts.join(',');
        if (wrap.pbKey === key) {
            return;
        }
        wrap.pbKey = key;
        if (!counts.length) {
            // a version without rows keeps the room of the rows it replaces while the switch runs (what lies below does not move under the hold); the room goes when it has settled
            wrap.style.minHeight = wrap.offsetHeight + 'px';
            wrap.innerHTML = '';
            keepRoom(view, wrap);
            return;
        }
        if (!wrap.firstChild) {
            wrap.style.minHeight = Math.round(counts.length * docRowHeight) + 'px';
        }
        api.getJSON(api.getUrl('PlayniteBridge/games/' + encodeURIComponent(id) + '/documents')).then(function (answer) {
            if (view.pbRequest !== request || wrap.pbKey !== key) {
                return;
            }
            var rows = answer && answer.rows || [];
            wrap.pbId = id;
            wrap.pbSeed = answer && answer.seed || 0;
            wrap.pbDictionary = dictionary;
            var holder = document.createElement('div');
            holder.innerHTML = rows.map(function (row) { return documentRow(row, dictionary); }).join('');
            wrap.style.minHeight = '';
            wrap.innerHTML = '';
            while (holder.firstChild) {
                wrap.appendChild(holder.firstChild);
            }
            // The tiles at once (the row has its height from the first frame, see loadScreenshots); the scroller is measured again in the next task.
            [].forEach.call(wrap.querySelectorAll('.pbDocRow'), function (section, index) {
                var row = rows[index];
                section.querySelector('.itemsContainer').innerHTML = documentCards(row.items, row.slotNames ? { names: row.slotNames, dictionary: dictionary, previous: null } : null, row.names, dictionary)
                    + (row.items.length < row.count ? docMoreCard(dictionary) : '');
            });
            if (rows.length && wrap.offsetHeight > 0) {
                docRowHeight = wrap.offsetHeight / rows.length;
            }
            setTimeout(function () {
                if (wrap.pbKey !== key) {
                    return;
                }
                [].forEach.call(wrap.querySelectorAll('.pbDocRow'), function (section) {
                    fillImages(section);
                    remeasureScroller(section);
                });
                watchPdfTiles(view);
            });
        }, function () {
            if (wrap.pbKey === key) {
                wrap.pbKey = null;
            }
        });
    }

    // Show Dividers of the Special Features row with Steam Special Features merged into it: a divider with the row's name before the local cards and
    // one with the Steam label before the Steam ones. Jellyfin draws the cards, so the dividers are put between them once the cards stand and again
    // when the container is redrawn (MutationObserver, paused while writing).
    function applySpecialsDividers(view) {
        var state = view.pbSpecialsDividers;
        var container = view.querySelector('#specialsContent');
        if (!container) {
            return;
        }
        if (view.pbSpecialsObserver) {
            view.pbSpecialsObserver.disconnect();
        }
        [].forEach.call(container.querySelectorAll(':scope > .pbConnGroup'), function (divider) {
            container.removeChild(divider);
        });
        if (state && state.steam) {
            var cards = [].slice.call(container.querySelectorAll(':scope > .card[data-id]'));
            var cardId = function (card) { return card.getAttribute('data-id').replace(/-/g, '').toLowerCase(); };
            var isSteam = function (card) { return !!state.steam[cardId(card)]; };
            var firstLocal = cards.filter(function (card) { return !isSteam(card); })[0];
            var firstSteam = cards.filter(isSteam)[0];
            var put = function (label, before) {
                var holder = document.createElement('div');
                holder.innerHTML = groupDivider(label, 'pbSpecialGroup');
                container.insertBefore(holder.firstChild, before);
            };
            // the Steam / local boundary whenever both are there (not bound to Keep Slots Together); with it a divider named like the row before each run of one type or slot
            // (then the first local run is named by its own row, else by the Special Features title)
            var runStarts = [], lastRun = null;
            cards.forEach(function (card) {
                if (isSteam(card)) {
                    lastRun = 'steam';
                    return;
                }
                var info = state.runs && state.runs[cardId(card)], run = info ? info.run : '';
                if (run !== lastRun) {
                    runStarts.push({ card: card, label: info ? info.label : state.localLabel });
                    lastRun = run;
                }
            });
            var boundary = !!(firstLocal && firstSteam);
            if (boundary) {
                if (!state.keep) {
                    put(state.localLabel, firstLocal);
                }
                put(state.steamLabel, firstSteam);
            }
            if (state.keep && (boundary || runStarts.length > 1)) {
                runStarts.forEach(function (start) {
                    put(start.label, start.card);
                });
            }
        }
        if (view.pbSpecialsObserver) {
            view.pbSpecialsObserver.observe(container, { childList: true });
        }
        applySpecialsTitle(view);
        remeasureScroller(view.querySelector('#specialsCollapsible'));
    }

    // The label under the Steam cards of the Special Features row with Steam Special Features merged into it (Before or After Local): Jellyfin draws the cards without a
    // second line of its own (ExtraType Unknown), so the second text line is written here, again when the container is redrawn (a MutationObserver of its children).
    function applySteamLabels(view) {
        var labels = view.pbSteamLabels;
        var container = view.querySelector('#specialsContent');
        if (!labels || !container) {
            return;
        }
        [].forEach.call(container.querySelectorAll(':scope > .card[data-id]'), function (card) {
            var key = labels[card.getAttribute('data-id').replace(/-/g, '').toLowerCase()];
            if (!key) {
                return;
            }
            var lines = card.querySelectorAll('.cardText');
            var line = lines[1];
            if (!line && lines[0]) {
                line = document.createElement('div');
                line.className = 'cardText cardTextCentered';
                lines[0].parentNode.appendChild(line);
            }
            var text = steamLabelText(key);
            if (line && line.textContent !== text) {
                line.innerHTML = steamLabelLink(text);
            }
        });
        applySpecialsTitle(view);
        remeasureScroller(view.querySelector('#specialsCollapsible'));
    }

    function loadSteamLabels(view, request, id, configuration) {
        var api = window.ApiClient;
        var on = !!(configuration && configuration.EnableVideos !== false && configuration.EnableSteamSpecialFeatures && configuration.SteamSpecialsMerge);
        if (!on) {
            view.pbSteamLabels = null;
            return;
        }
        api.getJSON(api.getUrl('PlayniteBridge/games/' + encodeURIComponent(id) + '/steamvideos')).then(function (items) {
            if (view.pbRequest !== request) {
                return;
            }
            var labels = {};
            (items || []).forEach(function (item) {
                labels[String(item.Id).replace(/-/g, '').toLowerCase()] = item.Etag || 'Clip';
            });
            view.pbSteamLabels = labels;
            if (!view.pbLabelsObserver) {
                view.pbLabelsObserver = new MutationObserver(function () {
                    applySteamLabels(view);
                });
            }
            var container = view.querySelector('#specialsContent');
            if (container) {
                view.pbLabelsObserver.disconnect();
                view.pbLabelsObserver.observe(container, { childList: true });
            }
            applySteamLabels(view);
        }, function () {});
    }

    function loadSpecialsDividers(view, request, id, configuration, dictionary, steamLabelText) {
        var api = window.ApiClient;
        var on = !!(configuration && configuration.EnableVideos !== false && configuration.VideosShowDividers);
        if (!on) {
            view.pbSpecialsDividers = null;
            applySpecialsDividers(view);
            return;
        }
        (view.pbVideoRows || api.getJSON(api.getUrl('PlayniteBridge/games/' + encodeURIComponent(id) + '/videorows'))).then(function (data) {
            if (view.pbRequest !== request) {
                return;
            }
            var steam = {}, runs = {};
            ((data && data.native) || []).forEach(function (card) {
                var cardId = String(card.id).replace(/-/g, '').toLowerCase();
                if (card.run === 'steam') {
                    steam[cardId] = true;
                } else {
                    runs[cardId] = { run: card.run, label: card.label || videoRowTitle({ key: card.run, label: '' }, dictionary) };
                }
            });
            view.pbSpecialsDividers = { steam: steam, runs: runs, keep: !!configuration.VideosKeepSlotsTogether, localLabel: labelOptions.specials || translate(dictionary, 'SpecialFeatures') || 'Special Features', steamLabel: steamLabelText || translate(dictionary, 'SteamSpecialFeatures') || 'Steam Special Features' };
            if (!view.pbSpecialsObserver) {
                view.pbSpecialsObserver = new MutationObserver(function () {
                    applySpecialsDividers(view);
                });
            }
            var container = view.querySelector('#specialsContent');
            if (container) {
                view.pbSpecialsObserver.observe(container, { childList: true });
            }
            applySpecialsDividers(view);
        }, function () {});
    }

    // A Steam Special Feature that failed in Jellyfin's player is reported once (POST steamvideos/refetch/{id}; the server asks Steam again at most
    // once per AppID in 6 hours), then its game's row is loaded again. Detected through the console.warn of Jellyfin's onPlaybackError, since its
    // 'playbackerror' event is not reachable from a plain script. Wrapped once, also when the script runs twice.
    if (!console.warn.pbSteamHook) {
        var consoleWarn = console.warn;
        console.warn = function (first, e, error) {
            try {
                var info = first === '[playbackmanager] onPlaybackError:' && error && error.streamInfo;
                var source = info && info.mediaSource;
                if (source && info.item && info.item.Type === 'Video' && source.Protocol === 'Http' && source.Container === 'hls') {
                    refetchSteam(info.item.Id);
                }
            } catch (ignored) {
            }
            return consoleWarn.apply(console, arguments);
        };
        console.warn.pbSteamHook = true;
    }

    function refetchSteam(itemId) {
        var api = window.ApiClient;
        if (!api) {
            return;
        }
        api.ajax({ type: 'POST', url: api.getUrl('PlayniteBridge/steamvideos/refetch/' + encodeURIComponent(itemId)) }).then(function (response) {
            return response.json();
        }).then(function (answer) {
            if (!answer || !answer.refetched) {
                return;
            }
            var key = extraIds[itemId];
            [].forEach.call(document.querySelectorAll('#itemDetailPage.pbGame'), function (view) {
                var args = view.pbSteamArgs;
                if (args && (!key || args.id.replace(/-/g, '').toLowerCase() === key)) {
                    loadSteamSpecials(view, view.pbRequest, args.id, args.order, args.enabled);
                }
            });
        }).catch(function () {
        });
    }

    // The image width setCardData picks for the shape overflowPortrait (see cardBuilderUtils getPostersPerRow).
    function cardWidth(tv) {
        var screenWidth = window.innerWidth;
        if (window.screen && window.screen.availWidth - screenWidth > 20) {
            screenWidth = Math.floor(screenWidth / 100) * 100;
        }
        var landscape = screenWidth > window.innerHeight * 1.3;
        var percent = tv ? 15.5 : landscape && screenWidth >= 1700 ? 11.6 : landscape ? 15.5 : screenWidth >= 1400 ? 15
            : screenWidth >= 1200 ? 18 : screenWidth >= 760 ? 23 : screenWidth >= 400 ? 31.5 : 42;
        return Math.round(screenWidth / (100 / percent));
    }

    function defaultBackgroundClass(name) {
        var code = String(name.slice(Math.floor(name.length / 2), Math.floor(name.length / 2) + 1).charCodeAt(0));
        var sum = 0;
        for (var i = 0; i < code.length; i++) {
            sum += parseInt(code.charAt(i), 10);
        }
        return 'defaultCardBackground defaultCardBackground' + ((parseInt(String(sum).slice(-1), 10) % 5) + 1);
    }

    // The tiles as cardBuilder.buildCard writes them, with the options of 12.1's renderItemCollections; a collection has no year, so its second line
    // is cardBuilder's empty line. Jellyfin's cardBuilder is a module of its bundle and cannot be called from here. A folder tile gets no type
    // indicator and its hover menu keeps only the link (view only).
    function collectionCards(items) {
        var layout = document.documentElement.classList;
        var tv = layout.contains('layout-tv');
        var api = window.ApiClient;
        var width = cardWidth(tv);
        var icon = '<span class="cardImageIcon material-icons folder" aria-hidden="true"></span>';
        var route = listRoute(visibleView());
        return items.map(function (item, index) {
            var name = escapeHtml(item.Name || '');
            var url = route + 'parentId=' + item.Id + '&serverId=' + item.ServerId;
            var tag = item.ImageTags && item.ImageTags.Primary;
            var imgUrl = tag ? api.getScaledImageUrl(item.Id, {
                type: 'Primary',
                fillHeight: item.PrimaryImageAspectRatio ? Math.round(width / item.PrimaryImageAspectRatio) : width / (2 / 3),
                fillWidth: width,
                quality: 96,
                tag: tag
            }) : null;
            var imageClass = 'cardImageContainer coveredImage' + (imgUrl ? '' : ' ' + defaultBackgroundClass(item.Name || '')) + ' cardContent';
            var lazy = imgUrl ? ' lazy" data-src="' + imgUrl + '"' : '"';
            var image = tv
                ? '<div class="' + imageClass + lazy + '>'
                : '<a href="' + url + '" data-action="link" class="' + imageClass + ' itemAction' + lazy + ' aria-label="' + name + '">';
            var title = tv ? name : '<a href="' + url + '" data-id="' + item.Id + '" data-serverid="' + item.ServerId + '" data-type="' + item.Type
                + '" data-mediatype="undefined" data-channelid="undefined" data-isfolder="' + item.IsFolder + '" class="itemAction textActionButton" title="'
                + name + '" data-action="link">' + name + '</a>';
            var footer = "<div class='cardText cardTextCentered cardText-first'><bdi>" + title + '</bdi></div>'
                + "<div class='cardText cardTextCentered'>&nbsp;</div>";
            var tagName = tv ? 'button' : 'div';
            var classes = 'card overflowPortraitCard' + (layout.contains('layout-desktop') ? ' card-hoverable' : '') + (tv ? ' show-focus show-animation' : '')
                + ' card-withuserdata' + (tv ? ' itemAction' : '');
            return '<' + tagName + ' data-index="' + index + '"' + (tv ? ' data-action="link"' : '') + ' data-isfolder="' + (item.IsFolder || false)
                + '" data-serverid="' + item.ServerId + '" data-id="' + item.Id + '" data-type="' + item.Type + '"'
                + (item.MediaType ? ' data-mediatype="' + item.MediaType + '"' : '') + ' data-context="movies" data-prefix="'
                + escapeHtml((item.SortName || item.Name || '').substring(0, 3).toUpperCase()) + '" class="' + classes + '"' + (tv ? ' aria-label="' + name + '"' : '') + '>'
                + '<div class="cardBox cardBox-bottompadded"><div class="cardScalable"><div class="cardPadder cardPadder-overflowPortrait">' + (imgUrl ? icon : '') + '</div>'
                + image + (imgUrl ? '' : icon) + (tv ? '</div>' : '</a>')
                + (layout.contains('layout-desktop') ? '<div class="cardOverlayContainer itemAction" data-action="link"><a href="' + url + '" class="cardImageContainer"></a></div>' : '')
                + '</div>' + footer + '</div></' + tagName + '>';
        }).join('');
    }

    // The tile images as imageLoader.fillImageElement sets them (Jellyfin's lazy loader is not reachable from here); fade-in follows the user's
    // fast-fade-in setting. Read only.
    function fillImages(container) {
        var fast = localStorage.getItem(window.ApiClient.getCurrentUserId() + '-fastFadein') !== 'false';
        var elems = container.querySelectorAll('.lazy[data-src]');
        [].forEach.call(elems, function (elem) {
            var url = elem.getAttribute('data-src');
            var preloader = new Image();
            preloader.src = url;
            elem.classList.add('lazy-hidden');
            elem.addEventListener('animationend', function onEnd() {
                elem.removeEventListener('animationend', onEnd);
                var padder = elem.parentNode && elem.parentNode.querySelector('.cardPadder');
                if (padder) {
                    padder.classList.add('lazy-hidden-children');
                }
            });
            preloader.addEventListener('load', function () {
                requestAnimationFrame(function () {
                    if (!elem.pbSnapshot) {
                        elem.style.backgroundImage = "url('" + url + "')";
                    }
                    elem.removeAttribute('data-src');
                    elem.classList.add(fast ? 'lazy-image-fadein-fast' : 'lazy-image-fadein');
                    elem.classList.remove('lazy-hidden');
                });
            });
        });
        remeasureScroller(container.closest ? container.closest('.verticalSection') || container : container);
    }

    // A built row inserted at its RowOrder place: before the next row of the order, else after the one before it, else before "More like this"; false without a place.
    function insertRow(view, order, key, section) {
        var keys = order.match(/Screenshots|Wallpapers|Documents|Steamspecials|Vrowsbefore|Specials|Videos|Soundtrack|Collections|Related|Actors|Connections|Similar/g) || [];
        var at = keys.indexOf(key);
        var present = function (other) { return !!tileRow(view, other); };
        var next = at < 0 ? null : keys.slice(at + 1).filter(present)[0];
        var prev = at < 0 ? null : keys.slice(0, at).filter(present).pop();
        var similar = view.querySelector('#similarCollapsible');
        if (next) {
            tileRow(view, next).parentNode.insertBefore(section, tileRow(view, next));
        } else if (prev) {
            tileRow(view, prev).parentNode.insertBefore(section, tileRow(view, prev).nextSibling);
        } else if (similar) {
            similar.parentNode.insertBefore(section, similar);
        } else {
            return false;
        }
        orderFlex(view, order);
        return true;
    }

    function removeRow(view, rowId) {
        var row = view.querySelector('#' + rowId);
        if (row) {
            row.parentNode.removeChild(row);
        }
    }

    // Connections: the series tiles, related media and cast of the game's game.nfo, each in its own row or all in one shared row, as configured. The
    // order lists are completed to every key; an unknown name counts as Connections.
    function connectionsConfig(configuration) {
        var c = configuration && configuration.Connections || {};
        var list = function (value, all) {
            var keys = String(value || '').split(',').map(function (k) { return k.trim().toLowerCase(); }).filter(function (k, i, a) { return all.indexOf(k) !== -1 && a.indexOf(k) === i; });
            return keys.concat(all.filter(function (k) { return keys.indexOf(k) === -1; }));
        };
        return {
            enabled: c.EnableConnections !== false,
            series: c.Series || 'OwnRow',
            media: c.Media || 'OwnRow',
            cast: c.Cast || 'OwnRow',
            name: c.Name || 'Connections',
            ownName: c.OwnName || '',
            mediaName: c.MediaName || 'RelatedMedia',
            mediaOwnName: c.MediaOwnName || '',
            castName: c.CastName || 'CastAndCrew',
            castOwnName: c.CastOwnName || '',
            dividers: c.ConnectionsShowDividers !== false,
            singleName: c.ConnectionsNameSingleGroup !== false,
            groupOrder: list(c.GroupOrder, ['series', 'media', 'cast']),
            mediaOrder: list(c.MediaOrder, ['collections', 'movies', 'tvshows'])
        };
    }

    var mediaKinds = { BoxSet: 'collections', Movie: 'movies', Series: 'tvshows' };
    function sortedMedia(entries, order) {
        var rank = function (entry) {
            var at = order.indexOf(mediaKinds[entry.item.Type]);
            return at === -1 ? order.length : at;
        };
        return entries.map(function (entry, index) { return { entry: entry, index: index }; }).sort(function (a, b) {
            return rank(a.entry) - rank(b.entry) || a.index - b.index;
        }).map(function (x) { return x.entry; });
    }

    // The titles of the three groups: the series group is named by the Series Term (Detail Pages), the others by the name chosen for them (a name of the list in the UI language, or the
    // typed text); the same titles stand on the own rows and on the dividers of the shared row.
    function seriesTitle(dictionary, display) {
        return display.seriesTerm || translate(dictionary, 'Series') || 'Series';
    }

    function mediaTitle(dictionary, conn) {
        var key = { RelatedMedia: 'RelatedMedia', Related: 'ConnectionsRelated', MoreFromTheSeries: 'MoreFromTheSeries' }[conn.mediaName];
        return conn.mediaName === 'Own' && conn.mediaOwnName || (key && translate(dictionary, key)) || { Related: 'Related', MoreFromTheSeries: 'More from the Series' }[conn.mediaName] || 'Related Media';
    }

    function castTitle(dictionary, conn) {
        var key = { CastAndCrew: 'HeaderCastAndCrew', Characters: 'Characters' }[conn.castName];
        return conn.castName === 'Own' && conn.castOwnName || (key && translate(dictionary, key)) || (conn.castName === 'Characters' ? 'Characters' : 'Cast & Crew');
    }

    function connectionsTitle(dictionary, conn) {
        return conn.name === 'Own' ? conn.ownName || translate(dictionary, 'ConnectionsConnections') || 'Connections'
            : translate(dictionary, 'Connections' + conn.name) || conn.name;
    }

    // The groups of the shared row [{ key, label, items | entries }]: a thin divider with the group's name before each group when there are several.
    function connectionsCards(groups, dictionary, gameId, dividers) {
        return groups.map(function (group) {
            var cards = group.key === 'series' ? collectionCards(group.items) : nfoCards(group.entries, dictionary, gameId);
            return (dividers && groups.length > 1 ? '<div class="pbConnGroup" role="presentation"><span title="' + escapeHtml(group.label) + '">' + escapeHtml(group.label) + '</span></div>' : '') + cards;
        }).join('');
    }

    function setConnectionsRow(view, order, title, groups, dictionary, gameId, dividers) {
        var signature = groups.map(function (group) {
            return group.key + ':' + (group.key === 'series' ? group.items : group.entries.map(function (entry) { return entry.item; })).map(function (item) { return item.Id; }).join(',')
                + (group.key === 'cast' ? group.entries.map(function (entry) { return (entry.role || '') + (entry.picture ? entry.picture.tag : ''); }).join(',') : '');
        }).join('|') + '|' + title + '|' + dividers + '|' + groups.map(function (group) { return group.label; }).join(',');
        var existing = view.querySelector('#pbConnectionsCollapsible');
        if (existing) {
            if (!groups.length) {
                existing.parentNode.removeChild(existing);
            } else if (existing.pbSignature !== signature) {
                existing.pbSignature = signature;
                existing.querySelector('.sectionTitle').textContent = title;
                existing.querySelector('.itemsContainer').innerHTML = connectionsCards(groups, dictionary, gameId, dividers);
                fillImages(existing);
            }
            return;
        }
        if (!groups.length) {
            return;
        }
        var holder = document.createElement('div');
        holder.innerHTML = '<div id="pbConnectionsCollapsible" class="verticalSection detailVerticalSection"><h2 class="sectionTitle sectionTitle-cards padded-right"></h2>'
            + '<div is="emby-scroller" class="padded-top-focusscale padded-bottom-focusscale no-padding" data-centerfocus="true"><div is="emby-itemscontainer" class="scrollSlider focuscontainer-x itemsContainer">'
            + connectionsCards(groups, dictionary, gameId, dividers) + '</div></div></div>';
        var section = holder.firstChild;
        section.querySelector('.sectionTitle').textContent = title;
        section.pbSignature = signature;
        if (insertRow(view, order, 'Connections', section)) {
            fillImages(section);
        }
    }

    // The three groups of a game page's links. Series: the collections of the game's series, from Jellyfin's own getItem answers (only with Show
    // Series As Tiles or Both, and not where 12.1 has its own Collections row); related media and cast: games/{id}/nfo. Each group goes to its own
    // row, to the shared row or nowhere; Enable Connections off = no group at all.
    function loadConnections(view, request, game, dictionary, display, id) {
        var api = window.ApiClient;
        var conn = display.connections;
        var order = display.rowOrder;
        var tiles = conn.enabled && display.series !== 'Text' && display.series !== 'Off' && !view.querySelector('#collectionsCollapsible');
        var ids = tiles ? game.series.filter(function (series) { return series.collectionId; }).map(function (series) { return series.collectionId; }) : [];
        var wantNfo = conn.enabled && (conn.media !== 'Off' || conn.cast !== 'Off');
        Promise.all([
            ids.length ? Promise.all(ids.map(function (collectionId) { return api.getItem(api.getCurrentUserId(), collectionId); })).catch(function () { return null; }) : Promise.resolve([]),
            wantNfo ? api.getJSON(api.getUrl('PlayniteBridge/games/' + encodeURIComponent(id) + '/nfo')).then(function (answer) { return answer || {}; }, function () { return {}; }) : Promise.resolve({})
        ]).then(function (results) {
            if (view.pbRequest !== request || !view.classList.contains('pbGame')) {
                return;
            }
            var series = results[0];
            var media = conn.enabled && conn.media !== 'Off' ? sortedMedia((results[1].related || []).map(function (item) { return { item: item }; }), conn.mediaOrder) : [];
            var cast = conn.enabled && conn.cast !== 'Off' ? results[1].actors || [] : [];
            var own = function (place, items) { return place === 'OwnRow' ? items : []; };
            if (series) {
                if (tiles && conn.series === 'OwnRow' && series.length) {
                    setCollections(view, series, seriesTitle(dictionary, display), order);
                } else {
                    removeRow(view, 'pbCollectionsCollapsible');
                }
            } else if (!tiles || conn.series !== 'OwnRow') {
                removeRow(view, 'pbCollectionsCollapsible');
            }
            setNfoRow(view, order, 'Related', mediaTitle(dictionary, conn), own(conn.media, media), dictionary, id);
            setNfoRow(view, order, 'Actors', castTitle(dictionary, conn), own(conn.cast, cast), dictionary, id);
            var shared = [];
            var groups = {
                series: { key: 'series', label: seriesTitle(dictionary, display), items: series || [], place: tiles ? conn.series : 'Off' },
                media: { key: 'media', label: mediaTitle(dictionary, conn), entries: media, place: conn.media },
                cast: { key: 'cast', label: castTitle(dictionary, conn), entries: cast, place: conn.cast }
            };
            conn.groupOrder.forEach(function (key) {
                var group = groups[key];
                if (conn.enabled && group.place === 'ConnectionsRow' && (group.items || group.entries).length) {
                    shared.push(group);
                }
            });
            setConnectionsRow(view, order, conn.singleName && shared.length === 1 ? shared[0].label : connectionsTitle(dictionary, conn), shared, dictionary, id, conn.dividers);
        });
    }

    // Jellyfin renders name, details and misc info in one pass, and renderName always sets innerHTML: the item counts as rendered once the name's
    // first node differs from the one seen at viewshow.
    function whenRendered(view, staleName, done) {
        var name = view.querySelector('.nameContainer');
        if (name.firstChild && name.firstChild !== staleName) {
            done();
            return;
        }
        var observer = new MutationObserver(function () {
            observer.disconnect();
            done();
        });
        observer.observe(name, { childList: true });
    }

    // RowOrder as the tokens orderRows and setCollections read; a value with none of them counts as the default order; a value without screenshots
    // gets them right after the special features.
    var ROW_TOKENS = ['videos', 'screenshots', 'wallpapers', 'documents', 'collections', 'related', 'actors', 'connections', 'soundtrack', 'similar'];

    function rowOrderTokens(value) {
        var tokens = String(value || '').split(',').map(function (k) { return k.trim().toLowerCase(); }).filter(function (k, i, all) {
            return ROW_TOKENS.indexOf(k) !== -1 && all.indexOf(k) === i;
        });
        if (!tokens.length) {
            tokens = ROW_TOKENS.slice();
        }
        ROW_TOKENS.forEach(function (k) {
            if (tokens.indexOf(k) === -1) {
                tokens.splice(tokens.indexOf('similar') === -1 ? tokens.length : tokens.indexOf('similar'), 0, k);
            }
        });
        // The entry Videos is three rows of the page, in the order of the Videos table: the Steam row of its own, Jellyfin's merged Special Features
        // row, then the rows of other types and slots.
        return tokens.map(function (k) { return k === 'videos' ? 'SteamspecialsVrowsbeforeSpecialsVideos' : k.charAt(0).toUpperCase() + k.slice(1); }).join('');
    }

    var requests = 0;

    // games/{id} as one shared request per id for the detail page's viewshow and the getItem wrapper: the parsed answer, null for 204 or any error
    // (never rejects). Shared only while it runs and 5 s after.
    var gameRequests = {};

    var notGames = {};

    // Whether Jellyfin's own answer for the item says it can be a game (Type Movie, LocationType Virtual), per id. loadGame asks the server only for
    // such an item, so other items' pages cost no games/{id} request. A failed getItem does not make loadGame wait; a page with no getItem call at
    // all is asked after 5 s. kindWaiters: callbacks of loadGame calls that came before the answer.
    var itemKinds = {};
    var kindWaiters = {};

    function noteItemKind(id, mayBeGame) {
        var key = String(id).replace(/-/g, '').toLowerCase();
        itemKinds[key] = mayBeGame;
        if (!mayBeGame) {
            notGames[key] = true;
        }
        (kindWaiters[key] || []).forEach(function (wake) {
            wake(mayBeGame);
        });
        delete kindWaiters[key];
    }

    // getItem of this item failed (rejected): Jellyfin's answer will not come, so nobody waits for it. The item is asked as before (it could have been
    // a game) unless the last ClientConfiguration answer says there is no games library; a later successful getItem corrects the kind (noteItemKind).
    function noteItemFailed(id) {
        var key = String(id).replace(/-/g, '').toLowerCase();
        if (key in itemKinds) {
            return;
        }
        var may = !lastConfig || !!lastConfig.LibraryId;
        itemKinds[key] = may;
        (kindWaiters[key] || []).forEach(function (wake) {
            wake(may);
        });
        delete kindWaiters[key];
    }

    function itemMayBeGame(key) {
        if (key in itemKinds) {
            return Promise.resolve(itemKinds[key]);
        }
        return new Promise(function (resolve) {
            var wake = function (answer) {
                clearTimeout(timer);
                resolve(answer);
            };
            var timer = setTimeout(function () {
                var waiting = kindWaiters[key];
                if (waiting) {
                    var at = waiting.indexOf(wake);
                    if (at !== -1) {
                        waiting.splice(at, 1);
                    }
                    if (!waiting.length) {
                        delete kindWaiters[key];
                    }
                }
                resolve(true);
            }, 5000);
            (kindWaiters[key] = kindWaiters[key] || []).push(wake);
        });
    }

    function loadGame(api, id) {
        var key = id.replace(/-/g, '').toLowerCase();
        if (!gameRequests[key]) {
            var promise = itemMayBeGame(key).then(function (may) {
                if (!may) {
                    return null;
                }
                return api.ajax({ type: 'GET', url: api.getUrl('PlayniteBridge/games/' + encodeURIComponent(id)) }).then(function (response) {
                    if (response && response.status === 204) {
                        notGames[key] = true;
                    }
                    return response && response.status === 200 ? response.json() : null;
                });
            }).catch(function () {
                return null;
            });
            gameRequests[key] = promise;
            promise.then(function () {
                setTimeout(function () {
                    if (gameRequests[key] === promise) {
                        delete gameRequests[key];
                    }
                }, 5000);
            });
        }
        return gameRequests[key];
    }

    // A hidden detail view stays in Jellyfin's view cache with its nodes, so its observers leave with the view and come back with a restored
    // viewshow.
    document.addEventListener('viewbeforehide', function (e) {
        var view = e.target;
        if (!view || !view.classList) {
            return;
        }
        if (view.id === 'itemDetailPage') {
            view.pbLeft = true;
            if (!(e.detail && e.detail.pbInPlace)) {
                if (hold) {
                    abortHold();
                }
                view.pbRelPending = false;
                view.pbCover = null;
                removeGhost(true);
                revokeCopies();
                releaseCoverShown();
                abortPrefetch(view, true);
                clearTimeout(view.pbQuiet);
                view.pbPending = null;
                view.pbSwitching = false;
                view.pbSwitchAt = 0;
                view.classList.remove('pbKeepSimilar');
                clearFlexOrder(view);
                [].forEach.call(view.querySelectorAll('[id$="Collapsible"]'), function (row) {
                    clearTimeout(row.pbBarTimer);
                    if (row.pbBarObserver) {
                        row.pbBarObserver.disconnect();
                        row.pbBarObserver = null;
                    }
                });
            }
            if (view.pbObserver) {
                view.pbObserver.disconnect();
                view.pbObserver = null;
            }
            if (view.pbLabelsObserver) {
                view.pbLabelsObserver.disconnect();
            }
            if (view.pbSpecialsObserver) {
                view.pbSpecialsObserver.disconnect();
            }
            if (view.pbPdfObserver) {
                view.pbPdfObserver.disconnect();
            }
            [].forEach.call(view.querySelectorAll('.cardScalable'), function (picture) {
                if (picture.pbDivider) {
                    picture.pbDivider.disconnect();
                    picture.pbDivider = null;
                }
            });
        }
        // The games library and list pages hold the same two observers while cached; a restored viewshow starts them again.
        stopMarkers(view);
        stopRowTitles(view);
    }, true);

    // The observers that left with the view (viewbeforehide) watch the special features row again, and the dividers follow their pictures again.
    function rearmRowObservers(view) {
        var container = view.querySelector('#specialsContent');
        if (container && view.pbLabelsObserver && view.pbSteamLabels) {
            view.pbLabelsObserver.observe(container, { childList: true });
        }

        if (container && view.pbSpecialsObserver && view.pbSpecialsDividers) {
            view.pbSpecialsObserver.observe(container, { childList: true });
        }

        sizeDividers(view);
        if (view.pbPdfObserver) {
            watchPdfTiles(view);
        }
    }

    // Everything of a game on a detail view: its classes, rows, buttons, collections row, marker and data attributes.
    function clearGamePage(view) {
        ['pbGame', 'pbKeepSimilar', 'pbNoGenres', 'pbNoDeveloper', 'pbNoPublisher', 'pbNoCollections', 'pbNoSimilar', 'pbNoVideoIcon', 'pbNoSubtext', 'pbNoTitle', 'pbNoTexts'].forEach(function (name) {
            view.classList.remove(name);
        });
        // Our collections row leaves before Jellyfin's rows are checked for their places (restoreRows), else it would stand between
        // scenes and "More like this" and move that row needlessly.
        removeRows(view);
        restoreRows(view);
        view.pbMarkerConfig = null;
        view.pbMarkerStrings = null;
        view.pbGame = null;
        abortPrefetch(view, true);
        ['data-pb-version', 'data-pb-installed', 'data-pb-store'].forEach(function (name) {
            view.removeAttribute(name);
        });
    }

    document.addEventListener('viewshow', function (e) {
        var view = e.target;
        if (!view || view.id !== 'itemDetailPage') {
            return;
        }
        view.pbLeft = false;
        rearmRowObservers(view);
        // A restored view: Jellyfin's renderTrackSelections has just hidden and emptied the version list; the chosen version stays.
        if (e.detail && e.detail.isRestored) {
            // Jellyfin caches a view under the URL it was made for; after a version switched on it (switchInPlace) it may show another
            // version than the URL asks for: that version is drawn on it again.
            var wanted = itemId();
            if (wanted && view.pbParams && typeof view.pbParams.id === 'string'
                && view.pbParams.id.replace(/-/g, '').toLowerCase() !== wanted.replace(/-/g, '').toLowerCase()
                && view.isConnected) {
                var hash = window.location.hash;
                Promise.resolve().then(function () {
                    switchInPlace(view, wanted, hash);
                });
                return;
            }
            if (view.classList.contains('pbGame') && view.pbRedo) {
                if (wanted && view.pbFirst) {
                    writeFirst(view.pbFirst, wanted.replace(/-/g, '').toLowerCase());
                }
                view.pbRedo();
                if (!view.pbObserver) {
                    watch(view, view.pbRequest, view.pbRedo);
                }
            }
            return;
        }

        if (view.pbObserver) {
            view.pbObserver.disconnect();
            view.pbObserver = null;
        }
        // A version switched on the standing view: the page keeps our classes, rows, buttons and marker until the new version's apply replaces them,
        // so nothing vanishes and returns; only the old redo must not run.
        var inPlace = view.pbInPlace;
        // The previous switch was still unrendered when this one began: its late render may be what this view's first render callback sees, which is
        // no proof that Jellyfin draws the wrong item (see renderedFor).
        var overlapped = !!view.pbOverlap;
        view.pbOverlap = false;
        view.pbInPlace = false;
        view.pbSwitching = !!inPlace;
        if (!inPlace) {
            clearGamePage(view);
        }
        view.pbRedo = null;
        if (!view.pbVersionHooked) {
            view.addEventListener('change', onVersionChange, true);
            view.addEventListener('click', onDetailLink, true);
            view.pbVersionHooked = true;
        }

        var id = itemId();
        var api = window.ApiClient;
        if (!id || !api) {
            return;
        }
        // The page's own item is the version shown first (a link to a secondary version shows that one).
        view.pbVersion = id.replace(/-/g, '').toLowerCase();
        // The version the page was opened with, kept across versions picked on it (Artwork on Version Change). A new view on a history entry an
        // in-place pick wrote carries it in history.state; it counts once the game's answer lists it as one of its versions.
        var carried = null;
        if (!inPlace) {
            view.pbFirst = view.pbVersion;
            view.pbFirstImages = null;
            var entry = window.history.state;
            carried = entry && typeof entry === 'object' && entry.pbFor === view.pbVersion && typeof entry.pbFirst === 'string' ? entry.pbFirst : null;
        }
        var openedFor = view.pbVersion;

        var request = ++requests;
        view.pbRequest = request;
        var staleName = view.querySelector('.nameContainer').firstChild;

        loadGame(api, id).then(function (game) {
            if (view.pbRequest !== request) {
                return;
            }
            if (!game) {
                if (inPlace) {
                    clearGamePage(view);
                }
                finishSwitch(view);
                return;
            }
            view.classList.add('pbGame');
            view.pbGame = game;
            if (carried && carried !== view.pbFirst && (game.versions || []).some(function (v) {
                return v && typeof v.id === 'string' && v.id.replace(/-/g, '').toLowerCase() === carried;
            })) {
                view.pbFirst = carried;
            } else if (!inPlace && !view.pbLeft && (itemId() || '').replace(/-/g, '').toLowerCase() === openedFor) {
                // every opening of a game page writes its first version into its history entry before Jellyfin draws it: the page's own id, also over
                // a stale or foreign entry
                writeFirst(view.pbFirst, openedFor);
            }
            view.pbVersion = game.version || view.pbVersion;
            var config = clientConfiguration(api).catch(function () {
                return null;
            });
            Promise.all([config, loadStrings()]).then(function (results) {
                var configuration = results[0];
                var dictionary = results[1];
                var libraryId = configuration && configuration.LibraryId;
                var display = {
                    series: configuration && configuration.SeriesDisplay || 'Tiles',
                    seriesTerm: configuration && configuration.SeriesTerm || null,
                    mediaInfo: configuration && configuration.MediaInfo || ['Year', 'Playtime', 'AgeRating', 'CommunityRating', 'CriticRating'],
                    secondaryInfo: configuration && configuration.SecondaryInfo || [],
                    markerPosition: configuration && configuration.MarkerPosition || 'TopLeft',
                    detailRows: configuration && configuration.DetailRows || ['Series', 'Genres', 'Developer', 'Publisher', 'Platform', 'Features', 'Size', 'Status'],
                    rowOrder: rowOrderTokens(configuration && configuration.RowOrder),
                    connections: connectionsConfig(configuration),
                    showSimilar: !(configuration && configuration.ShowMoreLikeThis === false),
                    showVideoIcon: !(configuration && configuration.ShowVideoIcon === false),
                    showSubtext: !(configuration && configuration.ShowSubtext === false),
                    showTitle: !(configuration && configuration.ShowTitle === false),
                    docFit: (configuration && configuration.DocumentsPictureFit) || 'Adaptive',
                    docLimit: configuration && configuration.DocumentsCropLimitPercent > 0 ? configuration.DocumentsCropLimitPercent : 20,
                    docThumbs: !(configuration && configuration.DocumentsGenerateThumbnails === false),
                    serverComics: !!(configuration && configuration.ServerReadsComics),
                    docTitle: !(configuration && configuration.DocumentsShowTitle === false),
                    docSubtext: !(configuration && configuration.DocumentsShowSubtext === false),
                    shotFit: (configuration && configuration.ScreenshotsPictureFit) || 'Adaptive',
                    shotLimit: configuration && configuration.ScreenshotsCropLimitPercent > 0 ? configuration.ScreenshotsCropLimitPercent : 20,
                    wpFit: (configuration && configuration.WallpapersPictureFit) || 'Adaptive',
                    wpLimit: configuration && configuration.WallpapersCropLimitPercent > 0 ? configuration.WallpapersCropLimitPercent : 20,
                    shotTitle: !!(configuration && configuration.ScreenshotsShowTitle),
                    shotSubtext: !!(configuration && configuration.ScreenshotsShowSubtext),
                    wpTitle: !!(configuration && configuration.WallpapersShowTitle),
                    wpSubtext: !!(configuration && configuration.WallpapersShowSubtext),
                    labels: {
                        specials: titleText(dictionary, configuration, 'SpecialFeatures'),
                        screenshots: titleText(dictionary, configuration, 'Screenshots'),
                        wallpapers: titleText(dictionary, configuration, 'Wallpapers'),
                        documents: titleText(dictionary, configuration, 'Documents'),
                        soundtrack: titleText(dictionary, configuration, 'Soundtrack')
                    },
                    steamLabel: titleText(dictionary, configuration, 'SteamSpecialFeatures')
                };
                whenRendered(view, staleName, function () {
                    if (view.pbRequest === request) {
                        if (inPlace && !inPlaceBroken && !overlapped && !view.pbPending && !view.pbLeft && !renderedFor(view, id, game)) {
                            inPlaceBroken = true;
                            try {
                                window.sessionStorage.setItem('pbInPlaceBroken', '1');
                            } catch (e) {
                            }
                            window.location.reload();
                            return;
                        }
                        var redo = function () {
                            apply(view, game, dictionary, libraryId, display);
                        };
                        view.pbRedo = redo;
                        redo();
                        // TV layout: our first button gets the focus Jellyfin gives its play button, since Jellyfin's autoFocuser runs before our
                        // button exists and a game's .btnPlay stays hidden. Not for a restored view or after a version picked on the standing view.
                        if (!inPlace && document.documentElement.classList.contains('layout-tv')) {
                            var starts = view.querySelectorAll('.pbAction');
                            for (var s = 0; s < starts.length; s++) {
                                if (starts[s].offsetParent !== null) {
                                    starts[s].focus({ preventScroll: true });
                                    break;
                                }
                            }
                        }
                        // A view left before it was drawn is hidden by now: it arms no observer, the restored viewshow does.
                        var hidden = view.pbLeft || view.classList.contains('hide');
                        if (!hidden) {
                            watch(view, request, redo);
                        }
                        // 12.1 fills its own Collections row (tileRow); ours only where the page has none (loadConnections).
                        // one loader that fails (an unexpected answer) must not stop the rows after it
                        [function () { loadConnections(view, request, game, dictionary, display, id); },
                            function () { loadVideoRows(view, request, id, display.rowOrder, configuration, dictionary); },
                            function () { loadSpecialsDividers(view, request, id, configuration, dictionary, display.steamLabel); },
                            function () { loadSteamLabels(view, request, id, configuration); },
                            function () { loadScreenshots(view, request, game, dictionary, display.rowOrder); },
                            function () { loadWallpapers(view, request, game, dictionary, display.rowOrder); },
                            function () { loadDocuments(view, request, game, dictionary, display.rowOrder); },
                            function () { loadSoundtrack(view, request, game, dictionary, display.rowOrder); }].forEach(function (load) {
                            try {
                                load();
                            } catch (e) {
                                console.error('Playnite Bridge: a row could not be drawn', e);
                            }
                        });
                        if (configuration) {
                            setMarkerStyle(configuration);
                            view.pbMarkerConfig = configuration;
                            view.pbMarkerStrings = dictionary;
                            if (!hidden) {
                                startMarkers(view, configuration, dictionary);
                            }
                        }
                        finishSwitch(view);
                    }
                });
            });
        }).catch(function () {
        });
    });

    // A parameter of the current route when the hash is that page, else null; both forms ('#/movies.html?...' on 10.10.7, '#/movies?...' on 12.1)
    // count.
    function routeParam(page, name) {
        var hash = window.location.hash;
        var current = /^#\/([^?]*)\?/.exec(hash);
        return current && current[1].replace(/\.html$/, '') === page.replace(/\.html$/, '')
            ? new URLSearchParams(hash.substring(hash.indexOf('?') + 1)).get(name) : null;
    }

    // Suggestions asks Movies/Recommendations without parentId: add the open library as pbTopParentId, which only Playnite Bridge reads; its Latest
    // row gets pbLatest=1. A tag grid asks Users/<id>/Items without parentId: add the parentId of the hash. All only when that library is the games
    // library (gamesLibraryKnown); any other request is left as Jellyfin made it.
    var getUrlWrapped = false;

    function wrapGetUrl(api) {
        if (getUrlWrapped) {
            return;
        }
        getUrlWrapped = true;
        var proto = Object.getPrototypeOf(api);
        var previous = proto.getUrl;
        proto.getUrl = function (name, params, serverAddress) {
            var noParent = !(params && ('parentId' in params || 'ParentId' in params));
            var topParentId = name === 'Movies/Recommendations' && noParent ? routeParam('movies.html', 'topParentId') : null;
            if (topParentId && !gamesLibraryKnown(topParentId.replace(/-/g, '').toLowerCase())) {
                topParentId = null;
            }
            if (topParentId) {
                params = Object.assign({}, params, { pbTopParentId: topParentId });
            }
            var latestParent = /^Users\/[^/]+\/Items\/Latest$/.test(name) ? routeParam('movies.html', 'topParentId') : null;
            if (latestParent && gamesLibraryKnown(latestParent.replace(/-/g, '').toLowerCase())) {
                params = Object.assign({}, params, { pbLatest: 1 });
            }
            // Our filter entries ("pb:...") are only sent when this script asks for them, only on a page of the games library (its id or a
            // collection folder's, as listIconRequest; ClientConfiguration has no LibraryId for an unsupported library, so no marker there).
            if ((name === 'Items/Filters' || name === 'Items/Filters2') && gamesLibraryRoute()) {
                params = Object.assign({}, params, { pbFilters: 1 });
            }
            if (listIconRequest(name, params)) {
                params = Object.assign({}, params, { pbIcon: 1 });
            }
            var tagParentId = noParent && /^Users\/[^/]+\/Items$/.test(name) && routeParam('list.html', 'type') === 'tag'
                ? routeParam('list.html', 'parentId') : null;
            if (tagParentId && !gamesLibraryKnown(tagParentId.replace(/-/g, '').toLowerCase())) {
                tagParentId = null;
            }
            if (tagParentId) {
                params = Object.assign({}, params, { ParentId: tagParentId });
            }
            return previous.call(this, name, params, serverAddress);
        };
    }

    // The square list view image on the games library's movies.html or list.html: the server answers it with the game's icon when it gets pbIcon=1.
    function listIconRequest(name, params) {
        return !!params && /^Items\/[^/]+\/Images\/Primary$/.test(name) && !!params.fillWidth && params.fillWidth === params.fillHeight
            && gamesLibraryRoute();
    }

    function gamesLibraryRoute() {
        var id = routeParam('movies.html', 'topParentId') || routeParam('list.html', 'parentId');
        return !!id && gamesRoute(lastConfig, id.replace(/-/g, '').toLowerCase());
    }

    // pbIcon=1 on the URL of a list item's image when the getUrl wrapper could not add it yet (no ClientConfiguration answer then).
    function addListIcon(image) {
        var url = image.getAttribute('data-src');
        var lazy = !!url;
        if (!lazy) {
            var match = /url\(["']?([^"')]+)["']?\)/.exec(image.style.backgroundImage || '');
            url = match ? match[1] : null;
        }
        if (!url || url.indexOf('/Images/Primary') === -1 || /[?&]pbIcon=1/.test(url)) {
            return;
        }
        url += (url.indexOf('?') === -1 ? '?' : '&') + 'pbIcon=1';
        if (lazy) {
            image.setAttribute('data-src', url);
        } else {
            image.style.backgroundImage = 'url("' + url + '")';
        }
    }

    // Suggestions titles its rows from the Movies/Recommendations answer. An answer asked with pbTopParentId (see wrapGetUrl) is kept on the visible
    // view, unchanged; all other requests pass through untouched. getJSON is a class method of the shipped apiclient.
    // The answers of getSpecialFeatures and getLocalTrailers are handed on unchanged; on the side a game's extra ids are kept in extraIds with their
    // game's id. Game-ness comes only from what is already known (the running games/{id} request, else the visible detail page with class pbGame), so
    // no request of its own. The id of the item the metadata editor shows is kept for the menu of its "..." button.
    var getJsonWrapped = false;
    var extraIds = {};
    var extrasUrl = /\/Items\/([0-9a-f-]{32,36})\/(SpecialFeatures|LocalTrailers)(\?|$)/i;
    var editorUrl = /\/Items\/([0-9a-f-]{32,36})\/MetadataEditor(\?|$)/i;
    var editorItemId = null;

    function wrapGetJson(api) {
        if (getJsonWrapped) {
            return;
        }
        getJsonWrapped = true;
        var proto = Object.getPrototypeOf(api);
        var previous = proto.getJSON;
        proto.getJSON = function (url) {
            var promise = previous.apply(this, arguments);
            var editor = typeof url === 'string' ? editorUrl.exec(url) : null;
            if (editor) {
                editorItemId = editor[1].replace(/-/g, '').toLowerCase();
            }
            var extras = typeof url === 'string' ? extrasUrl.exec(url) : null;
            if (extras) {
                var key = extras[1].replace(/-/g, '').toLowerCase();
                var page = document.querySelector('#itemDetailPage.pbGame:not(.hide)');
                var shown = itemId();
                var game = gameRequests[key] || Promise.resolve(page && shown && shown.replace(/-/g, '').toLowerCase() === key ? true : null);
                Promise.all([promise, game]).then(function (results) {
                    if (results[1] && Array.isArray(results[0])) {
                        results[0].forEach(function (item) {
                            extraIds[item.Id] = key;
                        });
                    }
                }, function () {
                });
                return promise;
            }
            // 12.1 sends the open library itself as ParentId: kept as well when that is the games library.
            var parent = typeof url === 'string' && url.indexOf('?') !== -1 ? new URLSearchParams(url.substring(url.indexOf('?') + 1)).get('ParentId') : null;
            if (typeof url !== 'string' || url.indexOf('Movies/Recommendations') === -1 || (url.indexOf('pbTopParentId=') === -1 && !parent)) {
                return promise;
            }
            return promise.then(function (recommendations) {
                var view = document.querySelector('.mainAnimatedPage:not(.hide)');
                if (view && (!parent || gamesRoute(lastConfig, parent.replace(/-/g, '').toLowerCase()))) {
                    view.pbRecommendations = recommendations;
                }
                return recommendations;
            });
        };
    }

    // The detail page renders the getItem answer at once, so a possible game (Type Movie, LocationType Virtual) is handed on only once games/{id},
    // our texts and ClientConfiguration are there; the page never shows Jellyfin's state for a game. Errors, 204 and other items pass unchanged.
    var getItemWrapped = false;

    // Artwork on Version Change (ClientConfiguration VersionArtwork, per picture type follow and keep): a version picked on the standing detail page
    // shows the picture of the version the page was opened with (view.pbFirst) where its row says so. Only the item's tags change; Jellyfin then
    // draws Items/<picked id>/Images/<type> with the first version's tag, which the server answers with that version's image, so the first render is
    // already the final one. ArtworkPlus applies the same rule to its own pictures.
    var VERSION_ART = [['Cover', 'Primary'], ['Backdrop', 'Backdrop'], ['Logo', 'Logo'], ['Clearart', 'Art']];

    function imagesOf(item, key) {
        return { id: key, tags: Object.assign({}, item.ImageTags), backdrops: (item.BackdropImageTags || []).slice(), hashes: item.ImageBlurHashes || {} };
    }

    // fetchFirst(id): the first version's item as Jellyfin answers it (unwrapped), asked only by a new view on a version picked before (Back to
    // its history entry), whose first version's tags were never seen on it; returns a promise then.
    function versionArtwork(item, game, config, fetchFirst) {
        if (!game || !item.Id) {
            return;
        }
        var key = String(item.Id).replace(/-/g, '').toLowerCase();
        var view = null;
        [].forEach.call(document.querySelectorAll('#itemDetailPage'), function (v) {
            if (!view && !v.pbLeft && v.pbFirst && v.pbParams && typeof v.pbParams.id === 'string' && v.pbParams.id.replace(/-/g, '').toLowerCase() === key) {
                view = v;
            }
        });
        if (!view) {
            return;
        }
        var own = imagesOf(item, key);
        if (view.pbFirst === key) {
            view.pbFirstImages = own;
            return;
        }
        var rules = config && config.VersionArtwork;
        if (!rules) {
            return;
        }
        var wanted = view.pbFirst;
        if (view.pbFirstImages && view.pbFirstImages.id === wanted) {
            applyFirst(item, own, view.pbFirstImages, rules, config);
            return;
        }
        return Promise.resolve(fetchFirst(wanted)).then(function (first) {
            if (first && view.pbFirst === wanted && !(view.pbFirstImages && view.pbFirstImages.id === wanted)) {
                view.pbFirstImages = imagesOf(first, wanted);
            }
            if (view.pbFirstImages && view.pbFirstImages.id === view.pbFirst) {
                applyFirst(item, own, view.pbFirstImages, rules, config);
            }
        }, noop);
    }

    function applyFirst(item, own, first, rules, config) {
        VERSION_ART.forEach(function (pair) {
            var rule = rules[pair[0]];
            var type = pair[1];
            if (!rule) {
                return;
            }
            var has = type === 'Backdrop' ? own.backdrops.length > 0 : !!own.tags[type];
            if (rule.follow && (has || !rule.keep)) {
                return;
            }
            if (type === 'Backdrop') {
                item.BackdropImageTags = first.backdrops.slice();
            } else if (first.tags[type]) {
                item.ImageTags = Object.assign({}, item.ImageTags);
                item.ImageTags[type] = first.tags[type];
            } else if (item.ImageTags) {
                item.ImageTags = Object.assign({}, item.ImageTags);
                delete item.ImageTags[type];
            }
            if (first.hashes[type]) {
                item.ImageBlurHashes = Object.assign({}, item.ImageBlurHashes);
                item.ImageBlurHashes[type] = Object.assign({}, item.ImageBlurHashes[type], first.hashes[type]);
            }
        });
    }

    function wrapGetItem(api) {
        if (getItemWrapped) {
            return;
        }
        getItemWrapped = true;
        var proto = Object.getPrototypeOf(api);
        var previous = proto.getItem;
        getItemRaw = previous;
        proto.getItem = function () {
            var client = this;
            var userId = arguments[0];
            var itemId = arguments[1];
            return previous.apply(this, arguments).then(function (item) {
                if (item && item.Id) {
                    noteItemKind(item.Id, item.Type === 'Movie' && item.LocationType === 'Virtual');
                }
                if (!item || item.Type !== 'Movie' || item.LocationType !== 'Virtual') {
                    return item;
                }
                return Promise.all([loadGame(client, item.Id), loadStrings(), clientConfiguration(client)]).then(function (results) {
                    return Promise.resolve(versionArtwork(item, results[0], results[2], function (id) {
                        return previous.call(client, userId, id);
                    })).then(function () {
                        return item;
                    });
                }, function () {
                    return item;
                });
            }, function (error) {
                if (itemId) {
                    noteItemFailed(itemId);
                }
                throw error;
            });
        };
    }

    // The wrappers stand before the first getItem of a view: viewshow in the capture phase on document runs before the view's own
    // listener (the detail controller's reload); at once when the script loads after the ApiClient. Our texts start loading here too.
    var stringsStarted = false;

    function hookApiClient() {
        var api = window.ApiClient;
        if (!api) {
            return;
        }
        wrapGetUrl(api);
        wrapGetJson(api);
        wrapGetItem(api);
        if (!stringsStarted) {
            stringsStarted = true;
            loadStrings();
        }
    }

    document.addEventListener('viewshow', hookApiClient, true);
    hookApiClient();

    // The Suggestions tab of the games library: the fixed titles of "Continue Watching" and "Recently Added Movies" and the titles of the
    // recommendation rows of the kept answer (row i = answer entry i). Only the h2 text changes; nothing when the count differs.
    function setRowTitles(view, dictionary) {
        setText(view.querySelector('#resumableSection h2.sectionTitle'), translate(dictionary, 'RecentlyPlayed'));
        var latest = view.querySelector('#recentlyAddedItems');
        setText(latest && latest.closest('.verticalSection').querySelector('h2.sectionTitle'), translate(dictionary, 'RecentlyAdded'));
        var rows = view.pbRecommendations;
        var sections = view.querySelectorAll('.recommendations > .verticalSection');
        if (!rows || rows.length !== sections.length) {
            return;
        }
        for (var i = 0; i < rows.length; i++) {
            var type = rows[i].RecommendationType;
            var key = type === 'SimilarToRecentlyPlayed' ? 'BecauseYouPlayed' : type === 'HasDirectorFromRecentlyPlayed' ? 'FromDeveloper' : null;
            if (key) {
                var name = rows[i].BaselineItemName || '';
                setText(sections[i].querySelector('h2.sectionTitle'), translate(dictionary, key).replace('{0}', function () {
                    return name;
                }));
            }
        }
    }

    // Jellyfin replaces the rows with innerHTML on every load (moviesrecommended.js loadSuggestions): the titles follow. The same
    // observer keeps the empty states (setEmptyTexts): the Collections tab writes its message anew on every load.
    function startRowTitles(view, dictionary) {
        stopRowTitles(view);
        var recommendations = view.querySelector('.recommendations');
        if (!recommendations) {
            return;
        }
        setRowTitles(view, dictionary);
        setEmptyTexts(view, dictionary);
        view.pbTitleObserver = new MutationObserver(function () {
            setRowTitles(view, dictionary);
            setEmptyTexts(view, dictionary);
        });
        view.pbTitleObserver.observe(recommendations, { childList: true });
        var collections = view.querySelector('#collectionsTab .itemsContainer');
        if (collections) {
            view.pbTitleObserver.observe(collections, { childList: true });
        }
    }

    function stopRowTitles(view) {
        if (view.pbTitleObserver) {
            view.pbTitleObserver.disconnect();
            view.pbTitleObserver = null;
        }
    }

    // ClientConfiguration, shared by all views for 30 s (each answer groups all games on the server). Once an answer is there it is
    // given at once, also while a newer one loads after the 30 s; only the very first call waits for the server.
    var configPromise = null;
    var configTime = 0;
    var lastConfig = null;
    var lastConfigTime = 0;

    // The games library id of the last ClientConfiguration answer, kept in localStorage as a hint (the answer decides). Before the first answer of a
    // page load it is the only thing that tells the script a library is the games library; without a hint nothing of ours touches a request or a
    // page. A library id is a 32 digit hex string.
    var HINT_KEY = 'pbGamesLibraryId';
    var ICON_KEY = 'pbGamesLibraryIcon';

    function libraryHint() {
        try {
            return window.localStorage.getItem(HINT_KEY);
        } catch (e) {
            return null;
        }
    }

    function rememberLibrary(config) {
        try {
            if (config && config.LibraryId) {
                window.localStorage.setItem(HINT_KEY, config.LibraryId);
                if (typeof config.GamesLibraryIcon === 'string') {
                    window.localStorage.setItem(ICON_KEY, config.GamesLibraryIcon);
                } else {
                    window.localStorage.removeItem(ICON_KEY);
                }
            } else {
                window.localStorage.removeItem(HINT_KEY);
                window.localStorage.removeItem(ICON_KEY);
            }
        } catch (e) {
        }
    }

    // The first answer of a browser without a hint comes after the library page asked for its rows and tags, so those requests went out unmarked.
    // When the answer proves that the open page is the games library (or one of its collection folders), the page loads once more, now with the hint:
    // never for another library's page, never when the hint could not be stored, never twice.
    function reloadOnFirstSight(config, hintBefore) {
        try {
            if (config && config.LibraryId && hintBefore !== config.LibraryId && libraryHint() === config.LibraryId && gamesLibraryRoute()) {
                window.location.reload();
            }
        } catch (e) {
        }
    }

    // Whether the library or collection folder id (32 hex, lower case) is the games library's: by the answer once there is one, else by the hint.
    // Every script rule that touches a request of a library page asks this first, so a foreign library is never changed.
    function gamesLibraryKnown(key) {
        return !!key && (lastConfig ? gamesRoute(lastConfig, key) : key === libraryHint());
    }

    // Games library icon: Jellyfin shows the movie icon for the games library. One <style id="pbLibraryIcon"> swaps it for the admin page's choice,
    // only for elements that carry the games library's literal id (drawer entry, home button, drawer link, library card). The id is the
    // ClientConfiguration LibraryId, before the first answer the stored hint, and must be 32 hex digits before it goes into a selector.
    // Paths on a 24 grid, filled, even-odd. Controller and Gamepad are Material Icons (c) Google, Apache License 2.0; Handheld and Tile are this
    // plugin's own.
    var ICON_PATHS = {
        Controller: 'M16.29 12.7Q16.59 12.98 17.02 12.98Q17.44 12.98 17.72 12.7Q18 12.42 18 12Q18 11.58 17.72 11.3Q17.44 11.02 17.02 11.02Q16.59 11.02 16.29 11.3Q15.98 11.58 15.98 12Q15.98 12.42 16.29 12.7ZM14.3 9.7Q14.58 9.98 15 9.98Q15.42 9.98 15.7 9.7Q15.98 9.42 15.98 9Q15.98 8.58 15.7 8.3Q15.42 8.02 15 8.02Q14.58 8.02 14.3 8.3Q14.02 8.58 14.02 9Q14.02 9.42 14.3 9.7ZM11.02 11.02V9.98H9V8.02H8.02V9.98H6V11.02H8.02V12.98H9V11.02ZM21.56 16.08Q21.75 17.25 20.98 18.12Q20.2 18.98 19.03 18.98Q18 18.98 17.25 18.23L15 15.98H9L6.75 18.23Q6 18.98 4.92 18.98Q3.84 18.98 3.12 18.26Q2.39 17.53 2.39 16.45Q2.39 16.41 2.41 16.27Q2.44 16.12 2.44 16.08L3.52 8.44Q3.7 6.98 4.83 6Q5.95 5.02 7.45 5.02H16.55Q18 5.02 19.12 6.02Q20.25 7.03 20.48 8.44Z',
        Gamepad: 'M16.5 9H21.98V15H16.5L13.5 12ZM9 16.5 12 13.5 15 16.5V21.98H9ZM7.5 9 10.5 12 7.5 15H2.02V9ZM15 7.5 12 10.5 9 7.5V2.02H15Z',
        Handheld: 'M7 2h10a2 2 0 0 1 2 2v16a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2zM7 4.5v7.5h10V4.5zM8.5 14h1v1.5H11v1H9.5V18h-1v-1.5H7v-1h1.5zM13.6 16.2a.9.9 0 1 0 1.8 0a.9.9 0 1 0-1.8 0zM15.6 14.4a.9.9 0 1 0 1.8 0a.9.9 0 1 0-1.8 0z',
        Tile: 'M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM8 9.5h1v2h2v1H9v2H8v-2H6v-1h2zM13.4 10.6a1.1 1.1 0 1 0 2.2 0a1.1 1.1 0 1 0-2.2 0zM15.4 13.6a1.1 1.1 0 1 0 2.2 0a1.1 1.1 0 1 0-2.2 0z'
    };

    function libraryIconId() {
        var id = lastConfig ? lastConfig.LibraryId : libraryHint();
        return typeof id === 'string' && /^[0-9a-f]{32}$/i.test(id) ? id : null;
    }

    function libraryIconChoice() {
        var choice = lastConfig ? lastConfig.GamesLibraryIcon : null;
        if (!lastConfig) {
            try {
                choice = window.localStorage.getItem(ICON_KEY);
            } catch (e) {
                choice = null;
            }
        }
        return typeof choice === 'string' && Object.prototype.hasOwnProperty.call(ICON_PATHS, choice) ? choice : null;
    }

    // The rules for a validated id and a choice of ICON_PATHS: the class icons are empty spans drawn by a ::before glyph, which gets a 1em box filled
    // with currentColor and masked by the icon; the experimental drawer's inline SVG gets the path by the CSS property d.
    function libraryIconCss(id, choice) {
        var path = ICON_PATHS[choice];
        var mask = 'url("data:image/svg+xml,' + encodeURIComponent('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24"><path fill-rule="evenodd" d="' + path + '"/></svg>') + '") center/contain no-repeat';
        var targets = [
            'a.navMenuOption[data-itemid="' + id + '" i]>.navMenuOptionIcon::before',
            'a.homeLibraryButton[href*="topParentId=' + id + '" i]>.homeLibraryIcon::before',
            '.card[data-id="' + id + '" i] .cardImageIcon::before'
        ];
        return targets.join(',') + '{content:"";display:block;width:1em;height:1em;background-color:currentColor;-webkit-mask:' + mask + ';mask:' + mask + '}' +
            '@media (forced-colors:active){' + targets.join(',') + '{forced-color-adjust:none}}' +
            'a[href*="topParentId=' + id + '" i] .MuiListItemIcon-root svg path{d:path("' + path + '");fill-rule:evenodd}';
    }

    function applyLibraryIcon() {
        var id = libraryIconId();
        var choice = id ? libraryIconChoice() : null;
        var element = document.getElementById('pbLibraryIcon');
        if (!choice) {
            if (element && element.parentNode) {
                element.parentNode.removeChild(element);
            }
            return;
        }
        var css = libraryIconCss(id, choice);
        if (!element) {
            element = document.createElement('style');
            element.id = 'pbLibraryIcon';
            (document.head || document.documentElement).appendChild(element);
        }
        if (element.textContent !== css) {
            element.textContent = css;
        }
    }

    // Who draws (admin page "Settings From", decided on the server): ArtworkPlus for the game pages' artwork, VideoOSD for the player artwork while
    // its addon runs in this tab (html[data-jvosd-artwork]) unless Playnite Bridge was chosen (PlayerArtworkSource).
    function apDraws(config) {
        return !!config && config.ArtworkSource === 'ArtworkPlus';
    }

    function osdDraws() {
        return document.documentElement.hasAttribute('data-jvosd-artwork') && !(lastConfig && lastConfig.PlayerArtworkSource === 'PlayniteBridge');
    }

    function clientConfiguration(api) {
        if (!configPromise || Date.now() - configTime > 30000) {
            var promise = api.getJSON(api.getUrl('PlayniteBridge/ClientConfiguration'));
            configPromise = promise;
            configTime = Date.now();
            promise.then(function (config) {
                var hintBefore = libraryHint();
                lastConfig = config;
                lastConfigTime = Date.now();
                rememberLibrary(config);
                applyLibraryIcon();
                reloadOnFirstSight(config, hintBefore);
            }, function () {
                if (configPromise === promise) {
                    configPromise = null;
                }
            });
        }
        return lastConfig ? Promise.resolve(lastConfig) : configPromise;
    }

    // Store icon markers (ClientConfiguration Markers: icons per tile id) on game cards; titles are the translated tooltips. List items always show
    // the number: with icons the script draws Jellyfin's badge from VersionCounts, with the number marker Jellyfin draws it itself. Tiles listed as
    // dimmed (Uninstalled) get class pbUninstalled, all others lose it. With the marker top right, a card holding Jellyfin's played check mark gets
    // pbBesidePlayed. Writes only what is missing or different.
    function markTiles(view, config, dimmed, titles) {
        var markers = config.Markers || {};
        var counts = config.VersionCounts || {};
        var tiles = view.querySelectorAll('.card[data-id], .listItem[data-id]');
        var gamesView = view.classList.contains('pbGames') || view.classList.contains('pbGamesList');
        for (var i = 0; i < tiles.length; i++) {
            var id = tiles[i].getAttribute('data-id');
            if (tiles[i].classList.contains('pbUninstalled') !== !!dimmed[id]) {
                tiles[i].classList.toggle('pbUninstalled', !!dimmed[id]);
            }
            var image = tiles[i].classList.contains('card') ? tiles[i].querySelector('.cardImageContainer') : null;
            var beside = !!image && config.MarkerPosition === 'TopRight' && !!image.querySelector('.cardIndicators .playedIndicator');
            if (image && image.classList.contains('pbBesidePlayed') !== beside) {
                image.classList.toggle('pbBesidePlayed', beside);
            }
            if (!tiles[i].classList.contains('card')) {
                var listImage = tiles[i].querySelector('.listItemImage');
                if (listImage && gamesView) {
                    addListIcon(listImage);
                }
                if (listImage && counts[id]) {
                    var badge = listImage.querySelector('.mediaSourceIndicator');
                    if (!badge) {
                        badge = document.createElement('div');
                        badge.className = 'mediaSourceIndicator';
                        listImage.insertBefore(badge, listImage.querySelector('.listItemIndicators'));
                    }
                    setText(badge, String(counts[id]));
                }
                continue;
            }
            if (markers[id] && image) {
                setMarker(image, config.MarkerPosition, markers[id], titles[id] || '');
            }
        }
    }

    // A marker in holder: each icon is one of Jellyfin's .mediaSourceIndicator badges in a wrapper at the corner position, title the tooltip.
    // Replaced only when corner, icons or tile width differ (data-pb-key), so the observers that call it start no loop.
    function setMarker(holder, position, icons, title) {
        var key = position + ':' + icons.join(',') + ':' + holder.clientWidth;
        var marker = holder.querySelector('.pbVersionMarker');
        if (marker && marker.getAttribute('data-pb-key') === key) {
            if (marker.title !== title) {
                marker.title = title;
            }
            return;
        }
        if (marker) {
            marker.parentNode.removeChild(marker);
        }
        marker = document.createElement('div');
        marker.className = 'pbVersionMarker pb' + position;
        marker.setAttribute('data-pb-key', key);
        marker.title = title;
        marker.pbIcons = icons;
        icons.forEach(function (icon) {
            var badge = document.createElement('div');
            badge.className = 'mediaSourceIndicator';
            if (icon === '…' || /^\d+$/.test(icon)) {
                badge.textContent = icon;
            } else {
                var img = document.createElement('img');
                img.src = window.ApiClient.getUrl('PlayniteBridge/icons/' + icon);
                img.alt = '';
                badge.appendChild(img);
            }
            marker.appendChild(badge);
        });
        holder.appendChild(marker);
        fitMarker(holder, marker, position);
    }

    // The server sends at most MaxMarkerIcons; a narrower tile drops badges from the end until the row fits and the last one left becomes an
    // ellipsis. The row ends one icon gap before the tile's edge, or before the played check mark. A tile not laid out yet is left; the resize
    // listener measures it later.
    function fitMarker(holder, marker, position) {
        var box = holder.getBoundingClientRect();
        if (!box.width) {
            return;
        }
        var gap = 3;
        var played = /^Top/.test(position) ? holder.querySelector('.cardIndicators .playedIndicator') : null;
        var right = played && position === 'TopLeft' ? played.getBoundingClientRect().left - gap : box.right - gap;
        var left = box.left + gap;
        var overflows = function () {
            var row = marker.getBoundingClientRect();
            return row.right > right + 0.5 || row.left < left - 0.5;
        };
        while (marker.children.length > 1 && overflows()) {
            marker.removeChild(marker.lastChild);
            var last = marker.lastChild;
            if (last.textContent !== '…') {
                last.textContent = '…';
            }
        }
    }

    var markerResize = 0;
    window.addEventListener('resize', function () {
        clearTimeout(markerResize);
        markerResize = setTimeout(function () {
            var markers = document.querySelectorAll('.pbVersionMarker');
            for (var i = 0; i < markers.length; i++) {
                if (markers[i].pbIcons && markers[i].parentNode) {
                    var position = (/pb(TopLeft|TopRight|BottomLeft|BottomRight)/.exec(markers[i].className) || [])[1];
                    setMarker(markers[i].parentNode, position, markers[i].pbIcons, markers[i].title);
                }
            }
        }, 150);
    });

    function startMarkers(view, config, dictionary) {
        stopMarkers(view);
        var dimmed = {};
        (config.Uninstalled || []).forEach(function (id) {
            dimmed[id] = true;
        });
        var titles = {};
        Object.keys(config.MarkerTitles || {}).forEach(function (id) {
            titles[id] = markerTitle(dictionary, config.MarkerTitles[id]);
        });
        var dim = config.DimStrength;
        if (typeof dim === 'number' && isFinite(dim) && dim >= 0 && dim <= 100) view.style.setProperty('--pbDim', String(dim / 100));
        else view.style.removeProperty('--pbDim');
        markTiles(view, config, dimmed, titles);
        if ((!config.Markers || !Object.keys(config.Markers).length) && !(config.Uninstalled || []).length && config.MarkerPosition !== 'TopRight'
            && !Object.keys(config.VersionCounts || {}).length) {
            return;
        }
        view.pbMarkerObserver = new MutationObserver(function (records) {
            if (ghostOnly(records)) {
                return;
            }
            markTiles(view, config, dimmed, titles);
        });
        view.pbMarkerObserver.observe(view, { childList: true, subtree: true });
    }

    function stopMarkers(view) {
        if (view.pbMarkerObserver) {
            view.pbMarkerObserver.disconnect();
            view.pbMarkerObserver = null;
        }
    }

    // An own text in place of Jellyfin's: Jellyfin's text is kept on the element, so resetOwnText gives it back when the view stops being a games
    // view.
    function setOwnText(elem, text) {
        if (!elem || !text) {
            return;
        }
        if (elem.textContent !== text) {
            elem.pbJellyfinText = elem.textContent;
            elem.textContent = text;
        }
        elem.pbOwnText = text;
    }

    function resetOwnText(elem) {
        if (elem && elem.pbOwnText) {
            if (elem.textContent === elem.pbOwnText && typeof elem.pbJellyfinText === 'string') {
                elem.textContent = elem.pbJellyfinText;
            }
            elem.pbOwnText = null;
        }
    }

    function visibleView() {
        return document.querySelector('.mainAnimatedPage:not(.hide)');
    }

    // The first tab of the games library is named 'Movies' by Jellyfin and drawn into the shared header during the view's own viewshow, anew whenever
    // another view had the tabs. Only its text changes, only for the visible view.
    function setGamesTab(view, dictionary) {
        if (visibleView() !== view) {
            return;
        }
        var tab = document.querySelector('.skinHeader .headerTabs .emby-tab-button[data-index="0"] .emby-button-foreground');
        setOwnText(tab, translate(dictionary, 'LibraryTabGames'));
        if (tab && tab.pbOwnText) {
            view.pbTab = tab;
        }
    }

    // The empty states of the games library: Suggestions (controllers/movies/movies.html, shown by moviesrecommended.js)
    // and Collections (moviecollections.js writes h1 and p into the tab's items container; only the p changes).
    function setEmptyTexts(view, dictionary) {
        setOwnText(view.querySelector('#suggestionsTab .noItemsMessage p'), translate(dictionary, 'MessageNoGameSuggestions'));
        setOwnText(view.querySelector('#collectionsTab .itemsContainer > .noItemsMessage.centerMessage > p'), translate(dictionary, 'MessageNoGameCollections'));
    }

    function resetGamesTexts(view) {
        resetOwnText(view.pbTab);
        view.pbTab = null;
        resetOwnText(view.querySelector('#suggestionsTab .noItemsMessage p'));
        resetOwnText(view.querySelector('#collectionsTab .itemsContainer > .noItemsMessage.centerMessage > p'));
    }

    // movies.html (tabs of a library): class pbGames; list.html (a collection's or genre's grid): class pbGamesList;
    // set only on views of the games library and its collection folders.
    document.addEventListener('viewshow', function (e) {
        var view = e.target;
        var api = window.ApiClient;
        if (!view || !api) {
            return;
        }

        if (view === reactLibraryPage()) {
            clientConfiguration(api).then(scheduleReact, function () {
            });
            loadStrings().then(scheduleReact);
            scheduleReact();
            return;
        }

        stopMarkers(view);
        stopRowTitles(view);
        if (e.detail && e.detail.isRestored && view.classList.contains('pbGame') && view.pbMarkerConfig) {
            startMarkers(view, view.pbMarkerConfig, view.pbMarkerStrings);
        }

        // A reused view starts without the classes; a restored one keeps them while the latest known configuration matches its route; the answer
        // below confirms or removes them.
        var moviesId = routeParam('movies.html', 'topParentId');
        var id = moviesId || routeParam('list.html', 'parentId');
        var key = id ? id.replace(/-/g, '').toLowerCase() : null;
        if (!(key && e.detail && e.detail.isRestored && gamesRoute(lastConfig, key))) {
            view.classList.remove('pbGames', 'pbGamesList');
        }
        // The games texts at once when the latest configuration and the texts are known (no "Movies" flash on a repeated visit), else Jellyfin's.
        var known = knownStrings();
        if (moviesId && known && gamesRoute(lastConfig, key)) {
            setGamesTab(view, known);
            setEmptyTexts(view, known);
        } else {
            resetGamesTexts(view);
        }
        if (!id) {
            view.pbGamesRequest = (view.pbGamesRequest || 0) + 1;
            return;
        }

        var request = (view.pbGamesRequest || 0) + 1;
        view.pbGamesRequest = request;
        clientConfiguration(api).then(function (config) {
            if (view.pbGamesRequest !== request) {
                return;
            }
            if (gamesRoute(config, key)) {
                view.classList.add(moviesId ? 'pbGames' : 'pbGamesList');
                setMarkerStyle(config);
                loadStrings().then(function (dictionary) {
                    if (view.pbGamesRequest !== request) {
                        return;
                    }
                    if (moviesId) {
                        setGamesTab(view, dictionary);
                        startRowTitles(view, dictionary);
                    }
                    startMarkers(view, config, dictionary);
                });
            } else {
                view.classList.remove('pbGames', 'pbGamesList');
                resetGamesTexts(view);
            }
        }).catch(function () {
            if (view.pbGamesRequest === request) {
                view.classList.remove('pbGames', 'pbGamesList');
                resetGamesTexts(view);
            }
        });
    });

    function gamesRoute(config, key) {
        return !!config && (config.LibraryId === key || (config.CollectionIds || []).indexOf(key) !== -1);
    }

    // Filter dialogs of the games library. The filter dialog (Movies and Favorites tab) and the filter menu (list.html) of a games view get class
    // pbGamesFilter, so #pbStyles hides the groups a game cannot use before the first paint. The server sends its filter entries ("pb:...") as tags
    // (dialog) or genres (menu); Jellyfin renders them with its own checkboxes, which are only moved here into groups of their own.
    var filterGroups = [
        // prefix, class of the own group, title (own table key), place (before or after Jellyfin's genres), Jellyfin's element with the translated
        // title (dialog: collapse; menu: heading) or null, and the kind of server name its entries show translated (serverName) or null
        ['pb:source:', 'pbSourceFilters', 'Platform', 'before', null, null, 'store'],
        // Playnite's features use the own key GameFeatures, so it never equals Jellyfin's group of the local extras (setExtrasTitles); the detail
        // page's row keeps Jellyfin's "Features".
        ['pb:feature:', 'pbFeatureFilters', 'GameFeatures', 'before', null, null, null],
        ['pb:status:', 'pbStatusFilters', 'CompletionStatus', 'before', null, null, 'status'],
        ['pb:rating:', 'pbRatingFilters', 'HeaderParentalRatings', 'after', '.officialRatingFilters', null, null],
        ['pb:tag:', 'pbTagFilters', 'Tags', 'after', '.tagFilters', null, null],
        ['pb:year:', 'pbYearFilters', 'HeaderYears', 'after', '.yearFilters', null, null]
    ];

    // A group's title as Jellyfin rendered it, already translated (the dialog's collapse title or the menu's heading); else the own table.
    function groupTitle(kind, dlg, group, dictionary) {
        var selector = kind === 'dialog' ? group[4] : group[5];
        var elem = selector ? dlg.querySelector(selector) : null;
        var title = elem ? (kind === 'dialog' ? elem.getAttribute('title') : elem.textContent) : null;
        return title || translate(dictionary, group[2]);
    }

    function gamesFilterKind(dlg) {
        var view = document.querySelector('.mainAnimatedPage:not(.hide)');
        if (!dlg || !view) {
            return null;
        }
        if (dlg.classList.contains('filterDialog') && view.classList.contains('pbGames')) {
            var tab = view.querySelector('.pageTabContent.is-active');
            var id = tab ? tab.id : null;
            return id === 'moviesTab' || id === 'favoritesTab' ? 'dialog' : null;
        }
        return dlg.querySelector('.basicFilterSection') && view.classList.contains('pbGamesList') ? 'menu' : null;
    }

    function filterEntry(kind, label) {
        var elem = label.querySelector(kind === 'dialog' ? 'input' : 'span');
        var value = elem ? (kind === 'dialog' ? elem.getAttribute('data-filter') : elem.textContent) : null;
        return value && value.indexOf('pb:') === 0 ? value : null;
    }

    function escapeHtml(text) {
        return (text == null ? '' : String(text)).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    // An own group in the markup of the dialog (emby-collapse) or the menu (verticalSection with h2); returns its checkbox list.
    function filterSection(kind, dlg, cssClass, title, parent, before) {
        var elem = dlg.querySelector('.' + cssClass);
        if (!elem) {
            var holder = document.createElement('div');
            holder.innerHTML = kind === 'dialog'
                ? '<div is="emby-collapse" title="' + escapeHtml(title) + '" class="' + cssClass + '"><div class="collapseContent"><div class="checkboxList"></div></div></div>'
                : '<div class="verticalSection verticalSection-extrabottompadding focuscontainer-x ' + cssClass + '"><h2 class="checkboxListLabel">'
                    + escapeHtml(title) + '</h2><div class="checkboxList checkboxList-verticalwrap"></div></div>';
            elem = holder.firstChild;
            parent.insertBefore(elem, before);
        }
        return elem.querySelector('.checkboxList');
    }

    // Stores in the order of the admin's platform list (SourceOrder); a name not in it keeps the server's order after them. Sorted here because
    // Jellyfin's filter dialog sorts the tags anew once one is checked.
    function sourceRank(order, name) {
        var index = order.indexOf(name);
        return index >= 0 ? index : order.length;
    }

    function compareSources(order, a, b) {
        return sourceRank(order, a) - sourceRank(order, b);
    }

    // Jellyfin's own checkboxes of the local extras per kind of the server's entries "pb:extra:<kind>": the id in the dialog, the data-settingname in
    // the menu.
    var extraFilters = {
        trailer: ['chkTrailer', 'HasTrailer'],
        special: ['chkSpecialFeature', 'HasSpecialFeature'],
        themesong: ['chkThemeSong', 'HasThemeSong'],
        themevideo: ['chkThemeVideo', 'HasThemeVideo']
    };

    // Jellyfin's group of the local extras shows only the checkboxes of the kinds the games have (kinds: { kind: true }), and the group
    // only with one of them (class pbExtrasShown, see #pbStyles); Subtitles stays hidden. Jellyfin's own class hide is not touched.
    function showExtraFilters(kind, dlg, kinds) {
        var section = dlg.querySelector(kind === 'dialog' ? '.features' : '.featureSection');
        if (!section) {
            return;
        }
        var any = false;
        Object.keys(extraFilters).forEach(function (key) {
            var label = kind === 'dialog' ? dlg.querySelector('#' + extraFilters[key][0]) : section.querySelector('[data-settingname="' + extraFilters[key][1] + '"]');
            label = label && kind === 'dialog' ? label.parentNode : label;
            if (label) {
                label.classList.toggle('pbHide', !kinds[key]);
                any = any || !!kinds[key];
            }
        });
        section.classList.toggle('pbExtrasShown', any);
    }

    // In the games library Jellyfin's group of the local extras (titled "Features") is titled "Special Features" and its entry of the special
    // features "Extras", as in Jellyfin's filter menu; Playnite's features are the own group "Game Features". Written into the drawn markup, since
    // emby-collapse writes the title into its button once.
    function setExtrasTitles(kind, dlg, dictionary) {
        var title = translate(dictionary, 'SpecialFeatures');
        var entry = translate(dictionary, 'Extras');
        var label = kind === 'dialog' ? dlg.querySelector('#chkSpecialFeature') : dlg.querySelector('.featureSection label[data-settingname="HasSpecialFeature"]');
        var text = label ? (kind === 'dialog' ? label.parentNode : label).querySelector('span') : null;
        if (title && kind === 'dialog') {
            var section = dlg.querySelector('.features');
            var heading = section ? section.querySelector('h3.emby-collapsible-title') : null;
            if (section) {
                section.setAttribute('title', title);
            }
            if (heading) {
                heading.textContent = title;
                heading.setAttribute('title', title);
            }
        } else if (title) {
            var menuHeading = dlg.querySelector('.featureSection > h2');
            if (menuHeading) {
                menuHeading.textContent = title;
            }
        }
        if (entry && text) {
            text.textContent = entry;
        }
    }

    function moveFilterEntries(kind, dlg, dictionary, sourceOrder) {
        var source = dlg.querySelector(kind === 'dialog' ? '.tagFilters' : '.genreFilters');
        var genres = dlg.querySelector('.genreFilters');
        var after = genres.nextElementSibling;
        var basic = dlg.querySelector(kind === 'dialog' ? '.checkboxList' : '.basicFilterSection .checkboxList');
        var found = {};
        var extraKinds = {};
        var labels = source.querySelectorAll('.filterOptions label');
        for (var i = 0; i < labels.length; i++) {
            var value = filterEntry(kind, labels[i]);
            var text = labels[i].querySelector('span');
            if (value && value.indexOf('pb:extra:') === 0) {
                extraKinds[value.substring('pb:extra:'.length)] = true;
                continue;
            }
            if (value === 'pb:installed' || value === 'pb:notinstalled') {
                // Installed and Not installed close the first list (Played, Unplayed, Favorites); without a text (no table) they stay hidden.
                var entryText = translate(dictionary, value === 'pb:installed' ? 'LabelInstalled' : 'LabelNotInstalled');
                if (!entryText) {
                    continue;
                }
                text.textContent = entryText;
                basic.appendChild(labels[i]);
                labels[i].classList.remove('pbHide');
                continue;
            }
            for (var g = 0; value && g < filterGroups.length; g++) {
                if (value.indexOf(filterGroups[g][0]) === 0) {
                    labels[i].pbName = value.substring(filterGroups[g][0].length);
                    text.textContent = filterGroups[g][6] ? serverName(dictionary, filterGroups[g][6], labels[i].pbName) : labels[i].pbName;
                    (found[g] = found[g] || []).push(labels[i]);
                }
            }
        }
        // Groups in the fixed order: Source, Features, Completion status (alphabetical as the server sends it), Genres (Jellyfin's), Parental Ratings, Tags, Years.
        filterGroups.forEach(function (group, g) {
            if (found[g]) {
                if (group[0] === 'pb:source:' && sourceOrder) {
                    found[g].sort(function (a, b) {
                        return compareSources(sourceOrder, a.pbName, b.pbName);
                    });
                }
                var list = filterSection(kind, dlg, group[1], groupTitle(kind, dlg, group, dictionary), genres.parentNode, group[3] === 'before' ? genres : after);
                found[g].forEach(function (label) {
                    list.appendChild(label);
                    label.classList.remove('pbHide');
                });
            }
        });
        if (!source.querySelector('.filterOptions label:not(.pbHide)')) {
            source.classList.add('hide');
        }
        showExtraFilters(kind, dlg, extraKinds);
    }

    // Jellyfin renders the tags (dialog) or genres (menu) once its Items/Filters(2) request returns: filter entries are
    // hidden and moved at once (an unknown "pb:" entry stays hidden).
    function watchFilterEntries(kind, dlg) {
        var options = dlg.querySelector((kind === 'dialog' ? '.tagFilters' : '.genreFilters') + ' .filterOptions');
        if (!options) {
            return;
        }
        var moving = false;
        function update() {
            var labels = options.querySelectorAll('label');
            var pending = false;
            for (var i = 0; i < labels.length; i++) {
                if (filterEntry(kind, labels[i])) {
                    labels[i].classList.add('pbHide');
                    pending = true;
                }
            }
            if (pending && !moving) {
                moving = true;
                var config = Promise.resolve().then(function () {
                    return clientConfiguration(window.ApiClient);
                }).catch(function () {
                    return null;
                });
                Promise.all([loadStrings(), config]).then(function (results) {
                    moving = false;
                    if (dlg.isConnected) {
                        moveFilterEntries(kind, dlg, results[0], results[1] && results[1].SourceOrder || null);
                    }
                });
            }
        }
        var observer = new MutationObserver(update);
        observer.observe(options, { childList: true });
        dlg.pbWatch = observer;
        update();
    }

    new MutationObserver(function (records) {
        records.forEach(function (record) {
            for (var r = 0; r < record.removedNodes.length; r++) {
                var gone = record.removedNodes[r];
                var closed = gone.classList && gone.classList.contains('dialogContainer') ? gone.firstElementChild : null;
                if (closed && closed.pbWatch) {
                    closed.pbWatch.disconnect();
                    closed.pbWatch = null;
                }
            }
            for (var i = 0; i < record.addedNodes.length; i++) {
                var node = record.addedNodes[i];
                var dlg = node.classList && node.classList.contains('dialogContainer') ? node.firstElementChild : null;
                var kind = gamesFilterKind(dlg);
                if (kind) {
                    dlg.classList.add('pbGamesFilter');
                    // The filter dialog's "Resumable" label has no class of its own (data-filter sits on its checkbox).
                    var resumable = dlg.querySelector('.chkStandardFilter[data-filter="IsResumable"]');
                    if (resumable) {
                        resumable.parentNode.classList.add('pbHide');
                    }
                    // Nor has its "Subtitles" label; a game never has subtitles (GameQuery.Run answers hasSubtitles with no game).
                    var subtitles = dlg.querySelector('#chkSubtitle');
                    if (subtitles) {
                        subtitles.parentNode.classList.add('pbHide');
                    }
                    var known = knownStrings();
                    if (known) {
                        setExtrasTitles(kind, dlg, known);
                    } else {
                        loadStrings().then(setExtrasTitles.bind(null, kind, dlg));
                    }
                    watchFilterEntries(kind, dlg);
                }
            }
        });
    }).observe(document.body, { childList: true });

    // The React library page of 12.1 Modern and 10.10.7 experimental: #moviesPage without the legacy tabs. Found by its markup only.
    function reactLibraryPage() {
        var pages = document.querySelectorAll('#moviesPage');
        for (var i = 0; i < pages.length; i++) {
            if (!pages[i].classList.contains('hide') && !pages[i].querySelector('.pageTabContent')) {
                return pages[i];
            }
        }
        return null;
    }

    // This page asks through @jellyfin/sdk (axios, XMLHttpRequest), past the ApiClient wrappers above. Only while the visible page is the React
    // library page of the games library and only for a request to this server whose parentId is that library: the Latest row gets pbLatest=1, the
    // filter requests get pbFilters=1; the answers of Movies/Recommendations and Items/Filters are read on the side and handed on unchanged. Every
    // other request passes untouched.
    // Before the first ClientConfiguration answer the games library is not known: a request of the library the last answer named (the localStorage
    // hint) waits in send() for the answer, is then opened again with the marker (open() drops the request headers, so these are set again) and sent;
    // after 3 s without an answer it goes as it was. A held request that is aborted meanwhile is not sent (abort() on an unsent request fires no
    // event, so the wrapper below notes it). Any other request is sent at once.
    // The wrappers chain to the methods current at hook time, hook once (pbBridgeWrapped on the prototype), only read answers, and fall back to the
    // previous method on an error in their own logic.
    var reactAnswers = { recommendations: null, filters: null };
    var xhrOpen = XMLHttpRequest.prototype.open;
    var xhrSetRequestHeader = XMLHttpRequest.prototype.setRequestHeader;
    var xhrSend = XMLHttpRequest.prototype.send;
    var xhrAbort = XMLHttpRequest.prototype.abort;

    if (!XMLHttpRequest.prototype.pbBridgeWrapped) {
        Object.defineProperty(XMLHttpRequest.prototype, 'pbBridgeWrapped', { value: true });

        XMLHttpRequest.prototype.open = function (method, url) {
            var request = null;
            try {
                this.pbPending = !lastConfig && gamesLibraryKnown(routeLibrary()) && reactRequest(url, routeLibrary())
                    ? { args: Array.prototype.slice.call(arguments), headers: [] } : null;
                request = reactRequest(url, lastConfig && lastConfig.LibraryId);
            } catch (e) {
                this.pbPending = null;
                request = null;
            }
            if (!request) {
                return xhrOpen.apply(this, arguments);
            }
            return openReact(this, arguments, request);
        };

        XMLHttpRequest.prototype.setRequestHeader = function () {
            try {
                if (this.pbPending) {
                    this.pbPending.headers.push(arguments);
                }
            } catch (e) {
                this.pbPending = null;
            }
            return xhrSetRequestHeader.apply(this, arguments);
        };

        XMLHttpRequest.prototype.send = function () {
            var pending = this.pbPending;
            if (!pending) {
                return xhrSend.apply(this, arguments);
            }
            this.pbPending = null;
            this.pbHeld = true;
            var xhr = this;
            var body = arguments;
            var released = false;
            var release = function () {
                if (released) {
                    return;
                }
                released = true;
                clearTimeout(timer);
                xhr.pbHeld = false;
                if (!xhr.pbAborted) {
                    sendHeld(xhr, pending, body);
                }
            };
            var timer = setTimeout(release, 3000);
            Promise.resolve().then(function () {
                return clientConfiguration(window.ApiClient);
            }).catch(function () {
            }).then(release);
        };

        XMLHttpRequest.prototype.abort = function () {
            if (this.pbHeld) {
                this.pbAborted = true;
            }
            return xhrAbort.apply(this, arguments);
        };
    }

    function sendHeld(xhr, pending, body) {
        var request = null;
        try {
            request = reactRequest(pending.args[1], lastConfig && lastConfig.LibraryId);
        } catch (e) {
            request = null;
        }
        if (request) {
            openReact(xhr, pending.args, request);
            pending.headers.forEach(function (header) {
                xhrSetRequestHeader.apply(xhr, header);
            });
        }
        xhrSend.apply(xhr, body);
    }

    function openReact(xhr, openArgs, request) {
        var args;
        try {
            args = Array.prototype.slice.call(openArgs);
            args[1] = request.url;
            if (request.read) {
                xhr.addEventListener('load', readReactAnswer.bind(null, xhr, request));
            }
        } catch (e) {
            return xhrOpen.apply(xhr, openArgs);
        }
        return xhrOpen.apply(xhr, args);
    }

    function routeLibrary() {
        var id = routeParam('movies', 'topParentId');
        return id ? id.replace(/-/g, '').toLowerCase() : null;
    }

    function reactRequest(url, libraryId) {
        var api = window.ApiClient;
        var key = routeLibrary();
        if (!api || !key || libraryId !== key || typeof url !== 'string' && !(url instanceof URL) || !reactLibraryPage()) {
            return null;
        }
        var text = String(url);
        var parsed;
        var server;
        try {
            parsed = new URL(text, window.location.href);
            server = new URL(api.serverAddress(), window.location.href);
        } catch (e) {
            return null;
        }
        var base = server.pathname.replace(/\/$/, '');
        if (parsed.origin !== server.origin || parsed.pathname.indexOf(base + '/') !== 0) {
            return null;
        }
        var path = parsed.pathname.substring(base.length);
        var parent = parsed.searchParams.get('parentId') || parsed.searchParams.get('ParentId');
        if (!parent || parent.replace(/-/g, '').toLowerCase() !== key) {
            return null;
        }
        var marker = /^\/(Users\/[^/]+\/)?Items\/Latest$/i.test(path) ? 'pbLatest' : /^\/Items\/Filters2?$/i.test(path) ? 'pbFilters' : null;
        var read = /^\/Movies\/Recommendations$/i.test(path) ? 'recommendations' : /^\/Items\/Filters$/i.test(path) ? 'filters' : null;
        if (!marker && !read) {
            return null;
        }
        if (marker && !parsed.searchParams.has(marker)) {
            text += (text.indexOf('?') === -1 ? '?' : '&') + marker + '=1';
        }
        return { url: text, read: read, key: key };
    }

    // Reads only; the Items/Filters answer gives the extras kinds and whether Playnite's plain tags are among its tags (Jellyfin's Tags group).
    function readReactAnswer(xhr, request) {
        try {
            var data = xhr.status === 200 ? (xhr.responseType === 'json' ? xhr.response : JSON.parse(xhr.responseText)) : null;
            if (request.read === 'recommendations' && Array.isArray(data)) {
                reactAnswers.recommendations = { key: request.key, rows: data };
            } else if (request.read === 'filters' && data) {
                var kinds = {};
                var plain = false;
                (data.Tags || []).forEach(function (tag) {
                    if (typeof tag === 'string' && tag.indexOf('pb:extra:') === 0) {
                        kinds[tag.substring('pb:extra:'.length)] = true;
                    }
                    plain = plain || typeof tag === 'string' && tag.indexOf('pb:') !== 0;
                });
                reactAnswers.filters = { key: request.key, kinds: kinds, plain: plain };
            }
            scheduleReact();
        } catch (e) {
            return;
        }
    }

    // The state of the React path: the page it marked, the text nodes it changed, the features rule it wrote; the filter popover it serves,
    // the entries read from its Tags group (captureTags), the classes of that group's region, whether withReactTags runs, the open own group.
    var react = { page: null, texts: [], style: null, filter: null, tagLabels: null, tagShell: null, tagsBusy: false, own: null };
    var noStrings = {};
    var reactScheduled = false;

    function scheduleReact() {
        if (!reactScheduled) {
            reactScheduled = true;
            Promise.resolve().then(function () {
                reactScheduled = false;
                updateReact();
            });
        }
    }

    function firstText(elem) {
        if (!elem) {
            return null;
        }
        var walker = document.createTreeWalker(elem, NodeFilter.SHOW_TEXT, null);
        var node;
        while ((node = walker.nextNode())) {
            if (node.nodeValue.trim()) {
                return node;
            }
        }
        return null;
    }

    // Own texts on React's text nodes, by nodeValue only (list: [node, text]); a node changed before and not in the list gets Jellyfin's text
    // back while it still shows ours (React writes its own text again whenever its value changes). An empty text (no table) leaves Jellyfin's.
    function setReactTexts(list) {
        var kept = [];
        list.forEach(function (entry) {
            var node = entry[0];
            var text = entry[1];
            if (!node || !text) {
                return;
            }
            if (node.nodeValue !== text) {
                node.pbJellyfinText = node.nodeValue;
                node.nodeValue = text;
            }
            node.pbOwnText = text;
            kept.push(node);
        });
        react.texts.forEach(function (node) {
            if (kept.indexOf(node) === -1 && node.pbOwnText) {
                if (node.nodeValue === node.pbOwnText && typeof node.pbJellyfinText === 'string') {
                    node.nodeValue = node.pbJellyfinText;
                }
                node.pbOwnText = null;
            }
        });
        react.texts = kept;
    }

    function setBodyClass(name, on) {
        if (document.body.classList.contains(name) !== on) {
            document.body.classList.toggle(name, on);
        }
    }

    // The active tab and the view menu: 12.1's menu (#library-view-menu, its .Mui-selected entry) or 10.10.7's tabs ([data-tab-index] with
    // aria-selected or Mui-selected); else the route's tab parameter. The default tab comes from the user's settings, so the markup counts first.
    function reactTab() {
        var items = document.querySelectorAll('#library-view-menu [role="menuitem"]');
        var index = null;
        for (var i = 0; i < items.length; i++) {
            if (items[i].classList.contains('Mui-selected')) {
                index = i;
            }
        }
        var selected = items.length ? null
            : document.querySelector('[role="tab"][data-tab-index][aria-selected="true"],.MuiTab-root.Mui-selected[data-tab-index]');
        if (selected) {
            index = parseInt(selected.getAttribute('data-tab-index'), 10);
        }
        if (index === null) {
            index = parseInt(routeParam('movies', 'tab') || '0', 10);
        }
        return { menu: items.length ? items : null, index: index };
    }

    // The own groups of the React filter popover, as in the legacy dialog before Jellyfin's genres; Installed and Not installed close Jellyfin's
    // first group. Each own checkbox is a copy of the React checkbox of its entry in Jellyfin's Tags group, which stays the only control: a click on
    // the copy clicks it, so React's filter state, request and "Reset filters" stay Jellyfin's.
    var reactGroups = filterGroups.filter(function (group) {
        return group[3] === 'before';
    });

    // A server entry's own text: Installed / Not installed, else its name, translated for the stores and completion states; null when unknown.
    function reactEntryText(value, dictionary) {
        if (value === 'pb:installed' || value === 'pb:notinstalled') {
            return translate(dictionary, value === 'pb:installed' ? 'LabelInstalled' : 'LabelNotInstalled');
        }
        for (var g = 0; g < reactGroups.length; g++) {
            if (value.indexOf(reactGroups[g][0]) === 0) {
                var name = value.substring(reactGroups[g][0].length);
                return reactGroups[g][6] ? serverName(dictionary, reactGroups[g][6], name) : name;
            }
        }
        return null;
    }

    // Jellyfin's group of the local extras shows only the kinds the games have (from the Items/Filters answer), the group only with one of them;
    // Jellyfin's Tags group is hidden when the answer has no plain tag of Playnite (only server entries, as the legacy dialog hides it).
    function setReactFeaturesStyle(kinds, plain) {
        var hidden = [];
        Object.keys(extraFilters).forEach(function (kind) {
            if (!kinds[kind]) {
                hidden.push(reactFilter + 'label:has(input[value="' + extraFilters[kind][1] + '"])');
            }
        });
        if (hidden.length === Object.keys(extraFilters).length) {
            hidden.push(reactFilter + '> * > :has(#filtersFeatures-header)');
        }
        if (plain === false) {
            hidden.push(reactFilter + '> * > :has(#filtersTags-header)');
        }
        var css = hidden.join(',') + '{display:none !important}';
        if (!react.style) {
            react.style = document.createElement('style');
            react.style.id = 'pbReactStyle';
            document.head.appendChild(react.style);
        }
        if (react.style.textContent !== css) {
            react.style.textContent = css;
        }
    }

    // The region of an accordion of the popover by its summary id (MUI Accordion: role region, aria-labelledby the summary), null while
    // collapsed (FilterButton.tsx: transition unmountOnExit).
    function reactRegion(headerId) {
        return document.querySelector('#filter-popover [aria-labelledby="' + headerId + '"]');
    }

    // React's expanded accordion summary of the popover (ids filters...-header, FilterButton.tsx), null when none is; the own ones start with pb.
    function expandedReactHeader(popover) {
        return popover.querySelector('[id^="filters"][id$="-header"][aria-expanded="true"]');
    }

    function childHolding(ancestor, elem) {
        while (elem && elem.parentNode !== ancestor) {
            elem = elem.parentNode;
        }
        return elem;
    }

    // Calls done with test()'s first truthy answer: checked after each of React's microtask renders (a click is a discrete event, React
    // renders it in a microtask), then every 16 ms, for about half a second.
    function whenReact(test, done, tries) {
        tries = tries || 0;
        var result = test();
        if (result || tries >= 40) {
            done(result);
            return;
        }
        (tries < 20 ? Promise.resolve() : new Promise(function (resolve) {
            setTimeout(resolve, 16);
        })).then(function () {
            whenReact(test, done, tries + 1);
        });
    }

    // Keeps the server entries of the mounted Tags group: [value, label markup] in React's order, and the classes of its region, details and
    // FormGroup for the own groups.
    function captureTags(region) {
        var labels = region.querySelectorAll('label');
        var list = [];
        for (var i = 0; i < labels.length; i++) {
            var input = labels[i].querySelector('input');
            if (input && input.value.indexOf('pb:') === 0 && input.value.indexOf('pb:extra:') !== 0) {
                list.push([input.value, labels[i].outerHTML]);
            }
        }
        var details = region.firstElementChild;
        var group = details ? details.firstElementChild : null;
        react.tagLabels = list;
        if (group) {
            react.tagShell = [region.className, details.className, group.className];
        }
    }

    // React mounts its Tags checkboxes only while that accordion is expanded, and keeps one accordion expanded. This expands Tags by a click on its
    // summary while body.pbTagsBusy hides it, runs action(region, done), keeps its entries, then restores what was expanded. React renders each click
    // in a microtask, so normally no step is painted.
    function withReactTags(popover, action) {
        var header = document.getElementById('filtersTags-header');
        if (!header || react.tagsBusy) {
            return;
        }
        var before = expandedReactHeader(popover);
        var hidden = before !== header;
        react.tagsBusy = true;
        if (hidden) {
            setBodyClass('pbTagsBusy', true);
            header.click();
        }
        var restore = function () {
            if (hidden) {
                (before || header).click();
            }
            whenReact(function () {
                return !hidden || header.getAttribute('aria-expanded') !== 'true';
            }, function () {
                requestAnimationFrame(function () {
                    requestAnimationFrame(function () {
                        react.tagsBusy = false;
                        setBodyClass('pbTagsBusy', false);
                        scheduleReact();
                    });
                });
            });
        };
        whenReact(reactRegion.bind(null, 'filtersTags-header'), function (region) {
            if (!region) {
                restore();
                return;
            }
            action(region, function () {
                captureTags(reactRegion('filtersTags-header') || region);
                restore();
            });
        });
    }

    // Clicks React's Tags checkbox of the entry (its onChange, FiltersTags.tsx) and waits for React's checked state. The click flips the
    // input's checked at once; React 17 (10.10.7) draws MUI's Mui-checked later, so the wait also needs the label's look to match.
    function toggleReactEntry(value) {
        if (!react.filter) {
            return;
        }
        withReactTags(react.filter, function (region, done) {
            var input = [].filter.call(region.querySelectorAll('input'), function (elem) {
                return elem.value === value;
            })[0];
            if (!input) {
                done();
                return;
            }
            var was = input.checked;
            var label = input.closest('label');
            input.click();
            whenReact(function () {
                return input.checked !== was && (!label || !!label.querySelector('.Mui-checked') === input.checked);
            }, done);
        });
    }

    // An own checkbox: the React label of the entry (its checked state and look) with the own text; its input loses the value, so the rules
    // and reads of React's entries never take it. A click (label or box) is cancelled and goes to React's checkbox.
    function ownEntry(value, html, text) {
        var holder = document.createElement('div');
        holder.innerHTML = html;
        var label = holder.firstElementChild;
        var input = label.querySelector('input');
        if (input) {
            input.removeAttribute('value');
        }
        var node = firstText(label);
        if (node) {
            node.nodeValue = text;
        }
        label.classList.add('pbReactEntry');
        label.pbKey = html + '\n' + text;
        label.addEventListener('click', function (e) {
            e.preventDefault();
            toggleReactEntry(value);
        });
        return label;
    }

    // The own checkboxes in list (entries: [value, html, text]) after React's children; rewritten only when they differ.
    function setOwnEntries(list, entries) {
        var current = [].filter.call(list.children, function (child) {
            return child.classList.contains('pbReactEntry');
        });
        if (current.length === entries.length && current.every(function (label, i) {
            return label.pbKey === entries[i][1] + '\n' + entries[i][2];
        })) {
            return;
        }
        current.forEach(function (label) {
            list.removeChild(label);
        });
        entries.forEach(function (entry) {
            list.appendChild(ownEntry(entry[0], entry[1], entry[2]));
        });
    }

    function toggleOwnGroup(prefix) {
        if (!react.filter || react.tagsBusy) {
            return;
        }
        var open = expandedReactHeader(react.filter);
        if (open && react.own !== prefix) {
            open.click();
        }
        react.own = react.own === prefix ? null : prefix;
        scheduleReact();
    }

    // An own accordion: the shell of Jellyfin's Tags accordion (root and summary copied at run time, ids and title own); its region holds the
    // own checkboxes while it is open. MUI's state classes Mui-expanded and Mui-focusVisible are set or removed by name.
    function ownGroup(paper, template, group, title) {
        var acc = paper.querySelector('.pbReactGroup[data-pb-group="' + group[1] + '"]');
        if (!acc) {
            acc = template.cloneNode(false);
            acc.classList.add('pbReactGroup');
            acc.setAttribute('data-pb-group', group[1]);
            var head = childHolding(template, document.getElementById('filtersTags-header')).cloneNode(true);
            acc.appendChild(head);
            var summary = head.id ? head : head.querySelector('[id]');
            summary.id = 'pb' + group[1] + '-header';
            summary.setAttribute('aria-controls', 'pb' + group[1] + '-content');
            summary.setAttribute('aria-expanded', 'false');
            summary.addEventListener('click', toggleOwnGroup.bind(null, group[0]));
            summary.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    toggleOwnGroup(group[0]);
                }
            });
            [acc].concat([].slice.call(acc.querySelectorAll('*'))).forEach(function (elem) {
                elem.classList.remove('Mui-expanded', 'Mui-focusVisible');
            });
        }
        var titleNode = firstText(acc.querySelector('[aria-controls="pb' + group[1] + '-content"]'));
        if (titleNode && titleNode.nodeValue !== title) {
            titleNode.nodeValue = title;
        }
        return acc;
    }

    // An own accordion open (summary and its parts with Mui-expanded, as React's, and the region with the own checkboxes) or closed.
    function setOwnGroupOpen(acc, group, open, entries) {
        var summary = acc.querySelector('[aria-controls="pb' + group[1] + '-content"]');
        if (summary.getAttribute('aria-expanded') !== String(open)) {
            summary.setAttribute('aria-expanded', String(open));
            [acc, summary].concat([].slice.call(summary.children)).forEach(function (elem) {
                elem.classList.toggle('Mui-expanded', open);
            });
        }
        var region = acc.querySelector('[role="region"]');
        if (!open || !react.tagShell) {
            if (region) {
                acc.removeChild(region);
            }
            return;
        }
        if (!region) {
            region = document.createElement('div');
            region.setAttribute('role', 'region');
            region.id = 'pb' + group[1] + '-content';
            region.setAttribute('aria-labelledby', 'pb' + group[1] + '-header');
            region.className = react.tagShell[0];
            region.innerHTML = '<div class="' + escapeHtml(react.tagShell[1]) + '"><div class="' + escapeHtml(react.tagShell[2]) + '"></div></div>';
            acc.appendChild(region);
        }
        setOwnEntries(region.firstElementChild.firstElementChild, entries);
    }

    // The filter popover of the games library (#filter-popover, mounted while open): its entries are read once when it opens and after "Reset
    // filters"; afterwards they change only through the own checkboxes or while Tags is open. Then the own groups before Jellyfin's Genres (else
    // Parental Ratings, else Tags), and Installed and Not installed in Jellyfin's first group. Writes only what differs.
    function updateReactFilters(popover, dictionary, config) {
        if (popover !== react.filter) {
            react.filter = popover;
            react.tagLabels = null;
            react.own = null;
        }
        var tagsHeader = popover ? document.getElementById('filtersTags-header') : null;
        if (!tagsHeader || react.tagsBusy) {
            return;
        }
        var tags = reactRegion('filtersTags-header');
        if (tags) {
            captureTags(tags);
        } else if (!react.tagLabels) {
            react.tagLabels = [];
            withReactTags(popover, function (region, done) {
                done();
            });
            return;
        }
        if (react.own && expandedReactHeader(popover)) {
            react.own = null;
        }
        var paper = childHolding(popover, tagsHeader);
        var template = paper ? childHolding(paper, tagsHeader) : null;
        if (!template) {
            return;
        }
        var anchor = ['filtersGenres-header', 'filtersOfficialRatings-header', 'filtersTags-header'].map(function (id) {
            return childHolding(paper, document.getElementById(id));
        }).filter(Boolean)[0];
        var order = config.SourceOrder || null;
        var next = anchor;
        for (var g = reactGroups.length - 1; g >= 0; g--) {
            var group = reactGroups[g];
            var entries = react.tagLabels.filter(function (entry) {
                return entry[0].indexOf(group[0]) === 0;
            }).map(function (entry) {
                return [entry[0], entry[1], reactEntryText(entry[0], dictionary)];
            });
            if (group[0] === 'pb:source:' && order) {
                entries.sort(function (a, b) {
                    return compareSources(order, a[0].substring(group[0].length), b[0].substring(group[0].length));
                });
            }
            var title = translate(dictionary, group[2]);
            var acc = paper.querySelector('.pbReactGroup[data-pb-group="' + group[1] + '"]');
            if (!entries.length || !title) {
                if (acc) {
                    paper.removeChild(acc);
                }
                continue;
            }
            acc = ownGroup(paper, template, group, title);
            if (acc.nextElementSibling !== next) {
                paper.insertBefore(acc, next);
            }
            setOwnGroupOpen(acc, group, react.own === group[0], entries);
            next = acc;
        }
        var status = reactRegion('filtersStatus-header');
        var list = status && status.firstElementChild ? status.firstElementChild.firstElementChild : null;
        if (list) {
            setOwnEntries(list, react.tagLabels.filter(function (entry) {
                return (entry[0] === 'pb:installed' || entry[0] === 'pb:notinstalled') && !!reactEntryText(entry[0], dictionary);
            }).map(function (entry) {
                return [entry[0], entry[1], reactEntryText(entry[0], dictionary)];
            }));
        }
    }

    // Jellyfin's "Reset filters" (12.1 FilterButton.tsx handleResetFiltersClick, the button with the Clear icon) empties React's filters: the
    // entries are read again once React rendered the click.
    document.addEventListener('click', function (e) {
        var button = react.filter && e.target.closest ? e.target.closest('#filter-popover button') : null;
        if (button && button.querySelector('svg[data-testid="ClearIcon"]')) {
            react.tagLabels = null;
            scheduleReact();
        }
    });

    // The square list image of a game in the React list: pbIcon=1 added to its src, as the legacy list gets it, only for this server's URLs of game
    // items. React writes src only when its prop changes and a new URL comes in a new <img>, which the childList observer reports.
    function setReactListIcons(page) {
        var server;
        try {
            server = new URL(window.ApiClient.serverAddress(), window.location.href);
        } catch (e) {
            return;
        }
        var base = server.pathname.replace(/\/$/, '');
        var images = page.querySelectorAll('.listItem[data-id][data-type="Movie"] .listItemImage img');
        for (var i = 0; i < images.length; i++) {
            var src = images[i].getAttribute('src');
            var parsed = null;
            try {
                parsed = src ? new URL(src, window.location.href) : null;
            } catch (e) {
                parsed = null;
            }
            if (parsed && parsed.origin === server.origin && parsed.pathname.indexOf(base + '/') === 0 && !parsed.searchParams.has('pbIcon')
                && /^\/Items\/[^/]+\/Images\/Primary$/i.test(parsed.pathname.substring(base.length))) {
                images[i].setAttribute('src', src + (src.indexOf('?') === -1 ? '?' : '&') + 'pbIcon=1');
            }
        }
    }

    // The Suggestions rows: first Jellyfin's rows that have items (Continue Watching, then Latest), then one row per entry of the read
    // Movies/Recommendations answer in its order; nothing when the counts do not fit.
    function reactRowTitles(page, key, dictionary, list) {
        var answer = reactAnswers.recommendations;
        var rows = answer && answer.key === key ? answer.rows : null;
        var sections = page.querySelectorAll('.verticalSection');
        var first = rows ? sections.length - rows.length : -1;
        if (first < 0 || first > 2) {
            return;
        }
        for (var i = 0; i < sections.length; i++) {
            var title = firstText(sections[i].querySelector('.sectionTitle'));
            if (i < first) {
                list.push([title, translate(dictionary, sections[i].querySelector('.overflowBackdropCard') ? 'RecentlyPlayed' : 'RecentlyAdded')]);
                continue;
            }
            var type = rows[i - first].RecommendationType;
            var name = rows[i - first].BaselineItemName || '';
            var rowKey = type === 'SimilarToRecentlyPlayed' ? 'BecauseYouPlayed' : type === 'HasDirectorFromRecentlyPlayed' ? 'FromDeveloper' : null;
            if (rowKey) {
                list.push([title, translate(dictionary, rowKey).replace('{0}', function () {
                    return name;
                })]);
            }
        }
    }

    function leaveReact() {
        setBodyClass('pbGamesReact', false);
        setBodyClass('pbGamesTiles', false);
        setReactTexts([]);
        if (react.page) {
            stopMarkers(react.page);
            react.page.pbReactMarkers = null;
            react.page = null;
        }
        react.filter = null;
    }

    // The games library on the React page: body classes, the first tab "Games", the empty states, the Suggestions titles, the own filter groups, list
    // icons, markers and dimming. A tab change or another library comes without a viewshow, so this runs after every batch of DOM changes and writes
    // only what differs.
    function updateReact() {
        var page = reactLibraryPage();
        var id = page ? routeParam('movies', 'topParentId') : null;
        var key = id ? id.replace(/-/g, '').toLowerCase() : null;
        var config = lastConfig;
        if (!page || !key || !config || config.LibraryId !== key) {
            leaveReact();
            return;
        }
        if (react.page !== page) {
            leaveReact();
            react.page = page;
        }
        var dictionary = knownStrings() || noStrings;
        var tab = reactTab();
        var trailers = !tab.menu && tab.index === 2;
        setBodyClass('pbGamesReact', true);
        setBodyClass('pbGamesTiles', !trailers);
        setMarkerStyle(config);
        if (page.pbReactMarkers !== config || page.pbReactStrings !== dictionary) {
            startMarkers(page, config, dictionary);
            page.pbReactMarkers = config;
            page.pbReactStrings = dictionary;
        }

        var texts = [];
        var gamesTab = translate(dictionary, 'LibraryTabGames');
        if (tab.menu) {
            texts.push([firstText(tab.menu[0]), gamesTab]);
            if (tab.index === 0) {
                texts.push([firstText(document.querySelector('[aria-controls="library-view-menu"]')), gamesTab]);
            }
        } else {
            texts.push([firstText(document.querySelector('[role="tab"][data-tab-index="0"]')), gamesTab]);
        }
        // The empty states (components/common/NoItemsMessage.tsx: h1 and p): Suggestions is tab 1 in both, Collections 3 (12.1) or 4 (10.10.7).
        var empty = firstText(page.querySelector('.noItemsMessage > p'));
        if (tab.index === 1) {
            texts.push([empty, translate(dictionary, 'MessageNoGameSuggestions')]);
            reactRowTitles(page, key, dictionary, texts);
        } else if (tab.index === (tab.menu ? 3 : 4)) {
            texts.push([empty, translate(dictionary, 'MessageNoGameCollections')]);
        }
        var popover = document.getElementById('filter-popover');
        if (popover) {
            // Jellyfin's "Features" are the local extras: "Special Features", as in the legacy dialog (setExtrasTitles).
            texts.push([firstText(document.getElementById('filtersFeatures-header')), translate(dictionary, 'SpecialFeatures')]);
        }
        setReactTexts(texts);
        var filters = reactAnswers.filters;
        setReactFeaturesStyle(filters && filters.key === key ? filters.kinds : {}, filters && filters.key === key ? filters.plain : null);
        updateReactFilters(popover, dictionary, config);
        setReactListIcons(page);
    }

    new MutationObserver(function () {
        if (react.page || document.getElementById('moviesPage')) {
            scheduleReact();
        }
    }).observe(document.body, { childList: true, subtree: true });
    window.addEventListener('hashchange', scheduleReact);
    window.addEventListener('popstate', scheduleReact);

    // View only: the video player's favourite heart gets class pbExtraRating (hidden by #pbStyles) while its data-id is a game's extra (extraIds).
    // Jellyfin sets data-id for every item it plays, so an attribute observer follows each change.
    function watchOsdRating(e) {
        var view = e.target;
        var button = view && view.id === 'videoOsdPage' ? view.querySelector('.btnUserRating') : null;
        if (!button || button.pbRatingObserver) {
            return;
        }
        var update = function () {
            button.classList.toggle('pbExtraRating', !!extraIds[button.getAttribute('data-id')]);
        };
        button.pbRatingObserver = new MutationObserver(update);
        button.pbRatingObserver.observe(button, { attributes: true, attributeFilter: ['data-id'] });
        update();
    }

    document.addEventListener('viewshow', watchOsdRating, true);

    // View only: a right click or long press ('contextmenu') and the 'menu' command of keyboard, gamepad and TV open Jellyfin's item menu or
    // multi-select from a tile; its 'play', 'resume' and 'record' commands start playback. On the tiles of the games views these stop here (capture
    // phase on document), except the playback commands on a game's extras (#specialsContent), which play like vanilla.
    function viewOnlyTile(target) {
        var tile = target && target.closest ? target.closest('.card[data-id], .listItem[data-id]') : null;
        return !!tile && viewOnlyScopes.some(function (scope) {
            return !!tile.closest(scope);
        });
    }

    document.addEventListener('contextmenu', function (e) {
        if (viewOnlyTile(e.target)) {
            e.preventDefault();
            e.stopPropagation();
        }
    }, true);

    document.addEventListener('command', function (e) {
        var command = e.detail && e.detail.command;
        var playback = command === 'play' || command === 'resume' || command === 'record';
        if ((command === 'menu' || playback) && viewOnlyTile(e.target) && !(playback && e.target.closest('#specialsContent'))) {
            e.stopPropagation();
            e.preventDefault();
        }
    }, true);

    // The games library's own tile, a .card carrying its id (home "My Media", the library overview, the card of Dashboard > Libraries); null
    // elsewhere and before the first ClientConfiguration answer.
    function gamesLibraryTile(target) {
        var tile = target && target.closest ? target.closest('.card[data-id]') : null;
        return tile && lastConfig && lastConfig.LibraryId && tile.getAttribute('data-id') === lastConfig.LibraryId ? tile : null;
    }

    // Whether the signed-in user is an administrator (Policy.IsAdministrator of ApiClient.getCurrentUser), asked at the start and again on
    // a viewshow with another user; null while unknown. The ClientConfiguration answer (the games library's id) is asked for at the same time.
    var adminState = { id: null, admin: null };

    function checkUser() {
        var api = window.ApiClient;
        var id = api ? api.getCurrentUserId() : null;
        if (!id || adminState.id === id) {
            return;
        }
        var state = adminState = { id: id, admin: null };
        clientConfiguration(api).catch(function () {});
        api.getCurrentUser().then(function (user) {
            state.admin = !!(user && user.Policy && user.Policy.IsAdministrator);
        }, function () {
            if (adminState === state) {
                adminState = { id: null, admin: null };
            }
        });
    }

    document.addEventListener('viewshow', checkUser, true);
    checkUser();

    function noAdmin() {
        return adminState.admin === false && !!window.ApiClient && adminState.id === window.ApiClient.getCurrentUserId();
    }

    // Holding the mouse button or a finger on a tile starts multi-select without a 'contextmenu' on desktop and iOS (a 550 ms timer on the items
    // container). On the games views' tiles and the games library's tile the timer is cleared here, after Jellyfin's listeners, by the container's
    // own 'mouseleave' handler; the event itself goes on, so swipes and drags stay. The click still opens the item.
    function stopHold(e) {
        if (viewOnlyTile(e.target) || gamesLibraryTile(e.target)) {
            // The React items container is a plain .itemsContainer with the same MultiSelect.
            var container = e.target.closest('[is="emby-itemscontainer"]') || e.target.closest('.itemsContainer');
            if (container) {
                container.dispatchEvent(new Event('mouseleave'));
            }
        }
    }

    document.addEventListener('mousedown', stopHold, { passive: true });
    document.addEventListener('touchstart', stopHold, { passive: true });

    // Where Jellyfin's browser.touch holds, every 'contextmenu' on an items container, a mouse right click as well as a long press, opens no menu and
    // starts multi-select. On the games library's tile it stops here then, doing what Jellyfin does on a touch-capable PC minus the multi-select; the
    // administrator's menu stays on the tile's "..." button. Without browser.touch only a 'contextmenu' from touch stops (pointerType, else
    // sourceCapabilities.firesTouchEvents, else a touch within the last second, as Firefox needs); a mouse right click passes.
    var jellyfinTouch = ('ontouchstart' in window) || navigator.maxTouchPoints > 0;
    var lastTouch = 0;

    ['touchstart', 'touchend', 'touchcancel'].forEach(function (type) {
        document.addEventListener(type, function () {
            lastTouch = Date.now();
        }, { passive: true, capture: true });
    });

    function fromTouch(e) {
        if (typeof e.pointerType === 'string' && e.pointerType) {
            return e.pointerType === 'touch';
        }
        if (e.sourceCapabilities) {
            return e.sourceCapabilities.firesTouchEvents;
        }
        return Date.now() - lastTouch < 1000;
    }

    document.addEventListener('contextmenu', function (e) {
        if ((jellyfinTouch || fromTouch(e)) && gamesLibraryTile(e.target)) {
            e.preventDefault();
            e.stopPropagation();
        }
    }, true);

    // Multi-select started on another tile gives every .card a selection panel: the games library's tile loses it at once, so it can neither be
    // selected nor show a check box.
    new MutationObserver(function (records) {
        records.forEach(function (record) {
            [].forEach.call(record.addedNodes, function (node) {
                if (node.classList && node.classList.contains('itemSelectionPanel') && gamesLibraryTile(node)) {
                    node.parentNode.classList.remove('withMultiSelect');
                    node.remove();
                }
            });
        });
    }).observe(document.body, { childList: true, subtree: true });

    // The games library's menus: Jellyfin's item menu of its tile, the sheet of its card in Dashboard > Libraries and the "..." sheet of its metadata
    // editor lose Shuffle, Select and Add to playlist and the dividers then left at an end or twice in a row; "Refresh metadata" or "Scan library"
    // (administrators only) gives way, at its place, to "Sync Playnite" and "Sync Artwork", which start the plugin's tasks as the admin page does and
    // report in Jellyfin's toast.
    // The item is remembered when its menu is asked for, and only counts for a sheet that opens within 2 s; the sheet is changed when
    // dialogHelper.open puts it into the page, before it is drawn. A user who is no administrator would find no entry left: the menu request on the
    // games library's tile stops here and no sheet opens. A sheet with no entry left is hidden at once and closed the way Jellyfin closes a dialog.
    var menuTileId = null;
    var menuTileAt = 0;
    var tileEntries = [
        { id: 'pbSyncPlaynite', icon: 'sync', text: 'SyncPlaynite', task: 'PlayniteBridgeSyncPlaynite', started: 'SyncStarted' },
        { id: 'pbSyncArtwork', icon: 'add_photo_alternate', text: 'SyncArtwork', task: 'PlayniteBridgeBuildGameImages', started: 'ArtworkSyncStarted' }
    ];

    function rememberMenuTile(target) {
        var tile = target && target.closest ? target.closest('.card[data-id]') : null;
        rememberMenuItem(tile ? tile.getAttribute('data-id') : null);
    }

    function rememberMenuItem(id) {
        menuTileId = id;
        menuTileAt = Date.now();
        if (menuTileId && window.ApiClient) {
            clientConfiguration(window.ApiClient).catch(function () {});
            loadStrings();
        }
    }

    function stopMenu(e) {
        e.stopPropagation();
        e.preventDefault();
    }

    document.addEventListener('command', function (e) {
        if (e.detail && e.detail.command === 'menu') {
            if (noAdmin() && gamesLibraryTile(e.target)) {
                stopMenu(e);
                return;
            }
            rememberMenuTile(e.target);
        }
    }, true);

    document.addEventListener('click', function (e) {
        var target = e.target && e.target.closest ? e.target : null;
        var button = target ? target.closest('[data-action="menu"], #mediaLibraryPage .btnCardMenu') : null;
        if (button && noAdmin() && gamesLibraryTile(button)) {
            stopMenu(e);
        } else if (button) {
            rememberMenuTile(button);
        } else if (target && target.closest('.formDialogHeader .btnMore')) {
            var dialog = target.closest('.dialog');
            rememberMenuItem(dialog && dialog.querySelector('.editItemMetadataForm') ? editorItemId : null);
        }
    }, true);

    // As startTask of the admin page: a task that is not Idle is not started again. The result goes to Jellyfin's toast through Dashboard.alert, as
    // the start buttons say a failure.
    function startTileTask(entry) {
        var api = window.ApiClient;
        api.getScheduledTasks().then(function (tasks) {
            var task = tasks.filter(function (t) {
                return t.Key === entry.task;
            })[0];
            if (!task) {
                throw new Error('no task');
            }
            if (task.State !== 'Idle') {
                return 'TaskAlreadyRunning';
            }
            return api.startScheduledTask(task.Id).then(function () {
                return entry.started;
            });
        }).catch(function () {
            return 'TaskStartFailed';
        }).then(function (key) {
            return loadStrings().then(function (dictionary) {
                window.Dashboard.alert(translate(dictionary, key));
            });
        });
    }

    function removeEntries(sheet) {
        ['shuffle', 'multiSelect', 'addtoplaylist'].forEach(function (id) {
            var entry = sheet.querySelector('.actionSheetMenuItem[data-id="' + id + '"]');
            if (entry) {
                entry.remove();
            }
        });
        var scroller = sheet.querySelector('.actionSheetScroller');
        var last = null;
        [].slice.call(scroller ? scroller.children : []).forEach(function (child) {
            var divider = child.classList.contains('actionsheetDivider');
            if (divider && (!last || last.classList.contains('actionsheetDivider'))) {
                child.remove();
            } else {
                last = child;
            }
        });
        if (last && last.classList.contains('actionsheetDivider')) {
            last.remove();
        }
    }

    function replaceRefresh(refresh, dictionary) {
        tileEntries.forEach(function (entry) {
            var button = refresh.cloneNode(true);
            button.setAttribute('data-id', entry.id);
            var icon = button.querySelector('.actionsheetMenuItemIcon');
            icon.classList.remove('refresh');
            icon.classList.add(entry.icon);
            button.querySelector('.actionSheetItemText').textContent = translate(dictionary, entry.text);
            button.addEventListener('click', function () {
                startTileTask(entry);
            });
            refresh.parentNode.insertBefore(button, refresh);
        });
        refresh.remove();
    }

    // A change of the sheet's entries. Focus on an entry that went (TV layout) moves to the first entry left. With an inline top the sheet is placed
    // again with its new height like actionSheet.ts getPosition does.
    function reshape(sheet, change) {
        var height = sheet.offsetHeight;
        var focused = sheet.contains(document.activeElement) ? document.activeElement : null;
        change();
        if (focused && !focused.isConnected) {
            var first = sheet.querySelector('.actionSheetMenuItem');
            if (first) {
                first.focus();
            }
        }
        var top = parseFloat(sheet.style.top);
        if (!isNaN(top)) {
            var now = sheet.offsetHeight;
            top += (height - now) / 2;
            if (top + now > window.innerHeight) {
                top -= top + now - window.innerHeight + 20;
            }
            sheet.style.top = Math.max(top, 10) + 'px';
        }
    }

    // On the first menu after a page load the answers may still be on their way: without the ClientConfiguration answer the whole change,
    // without the texts the refresh entry's, waits for them once (retried).
    function changeSheet(sheet, tileId, retried) {
        var refresh = sheet.querySelector('.actionSheetMenuItem[data-id="refresh"]');
        var dictionary = knownStrings();
        if (lastConfig && lastConfig.LibraryId === tileId) {
            reshape(sheet, function () {
                removeEntries(sheet);
                if (refresh && dictionary && dictionary.SyncPlaynite) {
                    replaceRefresh(refresh, dictionary);
                }
            });
            if (!sheet.querySelector('.actionSheetMenuItem')) {
                sheet.style.visibility = 'hidden';
                if (sheet.dialogContainer) {
                    sheet.dialogContainer.dispatchEvent(new Event('contextmenu', { cancelable: true }));
                }
            }
        }
        if ((!lastConfig || (lastConfig.LibraryId === tileId && refresh && !dictionary)) && !retried && window.ApiClient) {
            Promise.all([clientConfiguration(window.ApiClient), loadStrings()]).then(function () {
                if (sheet.isConnected) {
                    changeSheet(sheet, tileId, true);
                }
            }, function () {});
        }
    }

    new MutationObserver(function (records) {
        var tileId = menuTileId;
        if (!tileId) {
            return;
        }
        records.forEach(function (record) {
            [].forEach.call(record.addedNodes, function (node) {
                var sheet = node.classList && node.classList.contains('dialogContainer') ? node.querySelector('.actionSheet') : null;
                if (sheet) {
                    menuTileId = null;
                    if (Date.now() - menuTileAt < 2000) {
                        changeSheet(sheet, tileId);
                    }
                }
            });
        });
    }).observe(document.body, { childList: true });

    // Jellyfin 12.1's Dashboard > Libraries is React: each library is a card whose "..." opens an MUI Menu in a portal; no data-id, no action sheet.
    // The games library's card is known by its title, its "Scan library" entry by its RefreshIcon. That entry gives way to "Sync Playnite" and "Sync
    // Artwork" as copies in its own markup, which close the menu with Escape. Jellyfin's entry is React's node and is hidden (display none,
    // aria-disabled so the arrow keys skip it), not removed. A card not known for sure leaves the menu as Jellyfin drew it.
    var dashboardCard = null;
    var dashboardCardAt = 0;
    var libraryName = { id: null, promise: null };

    // The games library's name from Jellyfin's own item of the library (ClientConfiguration LibraryId), asked once per id; null without it.
    function gamesLibraryName(api) {
        var id = lastConfig && lastConfig.LibraryId;
        if (!id || !api) {
            return Promise.resolve(null);
        }
        if (libraryName.id !== id) {
            libraryName = { id: id, promise: api.getItem(api.getCurrentUserId(), id).then(function (item) {
                return item && item.Name || null;
            }, function () {
                return null;
            }) };
        }
        return libraryName.promise;
    }

    document.addEventListener('click', function (e) {
        var button = e.target && e.target.closest ? e.target.closest('#mediaLibraryPage .MuiCard-root .MuiIconButton-root') : null;
        var title = button ? button.closest('.MuiCard-root').querySelector('.MuiTypography-root') : null;
        if (title && window.ApiClient) {
            dashboardCard = title.textContent;
            dashboardCardAt = Date.now();
            clientConfiguration(window.ApiClient).then(function () {
                gamesLibraryName(window.ApiClient);
            }, function () {});
            loadStrings();
        }
    }, true);

    function changeMenu(menu, title) {
        var api = window.ApiClient;
        Promise.all([Promise.resolve().then(function () {
            return clientConfiguration(api);
        }).then(function () {
            return gamesLibraryName(api);
        }, function () {
            return null;
        }), loadStrings()]).then(function (results) {
            var icon = menu.querySelector('svg[data-testid="RefreshIcon"]');
            var scan = icon ? icon.closest('[role="menuitem"]') : null;
            var dictionary = results[1];
            if (!menu.isConnected || !results[0] || results[0] !== title || !scan || !dictionary.SyncPlaynite || menu.querySelector('.pbMenuEntry')) {
                return;
            }
            tileEntries.forEach(function (entry) {
                var item = scan.cloneNode(true);
                item.classList.add('pbMenuEntry');
                item.classList.remove('Mui-focusVisible');
                var svg = item.querySelector('svg');
                var glyph = document.createElement('span');
                glyph.className = 'material-icons ' + entry.icon;
                glyph.setAttribute('aria-hidden', 'true');
                svg.parentNode.replaceChild(glyph, svg);
                var text = item.querySelector('.MuiListItemText-primary') || item.querySelector('.MuiListItemText-root');
                if (text) {
                    text.textContent = translate(dictionary, entry.text);
                }
                item.addEventListener('click', function (e) {
                    e.stopPropagation();
                    menu.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
                    startTileTask(entry);
                });
                item.addEventListener('keydown', function (e) {
                    if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        item.click();
                    }
                });
                scan.parentNode.insertBefore(item, scan);
            });
            scan.style.display = 'none';
            scan.setAttribute('aria-disabled', 'true');
        });
    }

    new MutationObserver(function (records) {
        var title = dashboardCard;
        if (!title || Date.now() - dashboardCardAt >= 2000) {
            return;
        }
        records.forEach(function (record) {
            [].forEach.call(record.addedNodes, function (node) {
                var menu = node.querySelector ? node.querySelector('[role="menu"]') : null;
                if (menu) {
                    dashboardCard = null;
                    changeMenu(menu, title);
                }
            });
        });
    }).observe(document.body, { childList: true });

    // OSD & Artwork on game pages: poster size and anchor, the LogoArt chain and the CharacterArt positions with the values of ClientConfiguration
    // Artwork, mirroring ArtworkPlus. Artwork is null while ArtworkPlus is active, and nothing happens without it. Every page change removes the own
    // elements and stops the timers; a generation number keeps a late answer off the page.
    var artGeneration = 0;
    var artTimers = [];
    var ART_CSS =
        '#itemDetailPage.pbGame .detailLogo.pbLogoArtHidden{visibility:hidden !important}' +
        // LogoArt: centred under Jellyfin's logo slot, standing on the ribbon line and hidden where Jellyfin hides its logo.
        '.pbLogoArt{position:absolute;z-index:1;pointer-events:none;right:calc(37.5vw - var(--pb-offset, 0vw));top:calc(40vh - 7.2em + var(--pb-voffset, 0vh));transform:translate(50%,-100%)}' +
        '.pbLogoArt.pbLogoArtClearart{aspect-ratio:16/9;height:calc(25vw * var(--pb-size, 1) * 9 / 16)}' +
        '.pbLogoArtLayer,.pbCharArtLayer{position:absolute;inset:0;width:100%;height:100%;object-fit:contain;object-position:center bottom}' +
        // CharacterArt: top positions hang from the ribbon line beside the logo slot, bottom positions are fixed at the window's bottom edge.
        '.pbCharArtAnchor{position:absolute;height:0;top:calc(40vh - 7.2em);z-index:0;pointer-events:none}' +
        '.pbCharArtAnchor.pbCharArt-top-left{left:0;right:50vw}' +
        // from the left as ArtworkPlus (Core-v1.js): the scrollbar does not move it; --pb-page-left set on the anchor by artFrame
        '.pbCharArtAnchor.pbCharArt-top-right{left:calc(75vw - var(--pb-page-left, 0px));right:0}' +
        '.pbCharArtBox{pointer-events:none;--pb-fs:0vw}' +
        '.pbCharArtBox.pbCharArtFullscreen{--pb-fs:var(--pb-fs-offset)}' +
        '.pbCharArtAnchor>.pbCharArtBox{position:absolute;bottom:0}' +
        '.pbCharArt-top-left>.pbCharArtBox{right:calc(0px - var(--pb-offset) - var(--pb-fs))}' +
        '.pbCharArt-top-right>.pbCharArtBox{left:calc(var(--pb-offset) + var(--pb-fs))}' +
        // z-index 5: above the detail page's own layers, below .skinHeader, the drawer and its mask and dialogs, which all stack on body as well.
        '.pbCharArtBox.pbCharArt-bottom-left,.pbCharArtBox.pbCharArt-bottom-right{position:fixed;bottom:0;z-index:5}' +
        '.pbCharArtBox.pbCharArt-bottom-left{left:calc(0.8vw + var(--pb-page-left, 0px) + var(--pb-offset) + var(--pb-fs))}' +
        '.pbCharArtBox.pbCharArt-bottom-right{right:calc(0.8vw - var(--pb-offset) - var(--pb-fs))}' +
        '@media all and (max-height:31.25em){.pbLogoArt{top:calc(52vh - 7.2em + var(--pb-voffset, 0vh))}.pbCharArtAnchor{top:calc(52vh - 7.2em)}}' +
        '@media all and (max-width:68.75em){.pbLogoArt,.pbCharArtAnchor{display:none}}' +
        '.layout-mobile .pbLogoArt,.layout-tv .pbLogoArt,.layout-mobile .pbCharArtAnchor,.layout-tv .pbCharArtAnchor{display:none}';
    // Where a poster below 100 % sits inside the footprint of the full-size one (Default = Jellyfin's place).
    var POSTER_ANCHORS = {
        TopLeft: [0, 0], TopCenter: [0.5, 0], TopRight: [1, 0], CenterLeft: [0, 0.5], Center: [0.5, 0.5], CenterRight: [1, 0.5],
        BottomLeft: [0, 1], BottomCenter: [0.5, 1], BottomRight: [1, 1]
    };

    function artTimer(fn, ms) {
        artTimers.push(setTimeout(fn, ms));
    }

    function stopArt() {
        artGeneration++;
        artTimers.forEach(clearTimeout);
        artTimers = [];
        [].forEach.call(document.querySelectorAll('.pbLogoArt, .pbCharArtAnchor, .pbCharArtBox'), function (elem) {
            elem.remove();
        });
        [].forEach.call(document.querySelectorAll('.detailLogo.pbLogoArtHidden'), function (elem) {
            elem.classList.remove('pbLogoArtHidden');
        });
        [].forEach.call(document.querySelectorAll('.detailLogo'), function (elem) {
            elem.style.opacity = '';
            elem.style.transition = '';
        });
    }

    function setArtStyle(id, css) {
        var style = document.getElementById(id);
        if (!css) {
            if (style) {
                style.remove();
            }
            return;
        }
        if (!style) {
            style = document.createElement('style');
            style.id = id;
            document.head.appendChild(style);
        }
        if (style.textContent !== css) {
            style.textContent = css;
        }
    }

    // The poster size as one rule per layout and card shape in Jellyfin's own units (25vw box on desktop and TV, 30vw from the bottom on mobile);
    // empty at 100 %. 12.1 draws the card relative and floated with other top values per layout, so its container takes the card's place and the same
    // size and anchor rules apply as on 10.10.7.
    function posterCss(pct, anchor) {
        if (pct === 100) {
            return '';
        }
        var f = pct / 100;
        var a = pct < 100 && POSTER_ANCHORS[anchor] || [0, 0];
        var c = ' #itemDetailPage.pbGame .detailImageContainer.hide-mobile';
        var geometry = '.layout-desktop' + c + ',.layout-tv' + c + '{position:relative;z-index:3}' +
            '.layout-desktop' + c + '{top:-12.96em}' +
            '.layout-desktop' + c + '>.card{top:0}' +
            '.layout-tv' + c + '{top:18.75vw}' +
            '.layout-tv' + c + ':has(>.card.backdropCard){top:7.03125vw}' +
            '.layout-tv' + c + ':has(>.card.squareCard){top:12.5vw}' +
            '.layout-tv' + c + '>.card{top:0;transform:translateY(-50%)}' +
            '@media all and (max-width:62.5em){.layout-desktop' + c + '{top:0}}';
        return geometry + [['.layout-desktop', 25, false], ['.layout-tv', 25, false], ['.layout-mobile', 30, true]].map(function (lay) {
            var root = lay[0] + ' #itemDetailPage.pbGame .detailImageContainer ';
            var css = root + '.card{width:' + lay[1] * f + 'vw;max-width:' + lay[1] * f + 'vw;margin-left:' + lay[1] * (1 - f) * a[0] + 'vw}';
            return css + [['', 1.5], ['.backdropCard', 0.5625], ['.squareCard', 1]].map(function (shape) {
                var shift = lay[1] * shape[1] * (1 - f) * (lay[2] ? 1 - a[1] : a[1]);
                return root + '.card' + shape[0] + '{margin-' + (lay[2] ? 'bottom' : 'top') + ':' + shift + 'vw}';
            }).join('');
        }).join('');
    }

    function logoChain(a) {
        var chain = [];
        var stages = [a.ArtLogoArtSource, a.ArtLogoArtFallback, a.ArtLogoArtSecondFallback];
        for (var i = 0; i < stages.length && stages[i] !== 'None'; i++) {
            if (chain.indexOf(stages[i]) < 0) {
                chain.push(stages[i]);
            }
            if (stages[i] === 'Hide') {
                break;
            }
        }
        return chain;
    }

    function logoUntouched(a) {
        return a.ArtLogoArtSource === 'VanillaLogo' && a.ArtLogoArtLogoSizePercent === 100 && !a.ArtLogoArtLogoOffsetVw && !a.ArtLogoArtLogoVerticalOffsetVh &&
            logoChain(a).every(function (kind) {
                return kind === 'VanillaLogo' || kind === 'Hide';
            });
    }

    function logoCss(a) {
        var f = a.ArtLogoArtLogoSizePercent / 100;
        return f === 1 && !a.ArtLogoArtLogoOffsetVw && !a.ArtLogoArtLogoVerticalOffsetVh ? '' :
            '#itemDetailPage.pbGame .detailLogo{width:calc(25vw * ' + f + ');height:calc(16vh * ' + f + ');right:calc(37.5vw - 12.5vw * ' + f + ' - ' +
            a.ArtLogoArtLogoOffsetVw + 'vw);top:calc(10vh + ' + a.ArtLogoArtLogoVerticalOffsetVh + 'vh)}';
    }

    function sizeArtBox(box, a, prefix, ratio) {
        if (a[prefix + 'ScaleMode'] === 'Width') {
            box.style.width = a[prefix + 'WidthVw'] + 'vw';
            box.style.height = a[prefix + 'MaxHeightVh'] > 0 ? a[prefix + 'MaxHeightVh'] + 'vh' : 'auto';
        } else {
            box.style.height = a[prefix + 'HeightVh'] + 'vh';
            box.style.width = a[prefix + 'MaxWidthVw'] > 0 ? a[prefix + 'MaxWidthVw'] + 'vw' : 'auto';
        }
        box.style.aspectRatio = box.style.height === 'auto' || box.style.width === 'auto' ? String(ratio > 0 ? ratio : 1) : '';
    }

    function artRotation(a, prefix, align) {
        var multi = a[prefix + 'MultiImage'];
        var once = multi && a[prefix + 'SinglePass'];
        var cycle = Math.max(250, a[prefix + 'CycleTimeMs']);
        return {
            multi: multi,
            once: once,
            order: a[prefix + 'OrderMode'],
            randomStart: multi && !once && a[prefix + 'OrderMode'] === 'Sequential' && a[prefix + 'RandomStart'],
            stay: a[prefix + 'StaySingleImageStatic'],
            cycle: cycle,
            fade: Math.min(a[prefix + 'FadeTimeMs'], cycle),
            position: (align === 'Left' ? 'left' : align === 'Right' ? 'right' : 'center') + ' bottom'
        };
    }

    // Random Start: the list rotated so that any of its n images comes first, each with the same chance.
    function randomStartRotate(list) {
        var k = Math.floor(Math.random() * list.length);
        return list.slice(k).concat(list.slice(0, k));
    }

    // The images in the chosen order: Shuffle once without repeats, Random drawn with repeats, Sequential as listed, with a random start.
    function artOrder(urls, r) {
        var list = urls.slice();
        if (r.order === 'Shuffle') {
            for (var i = list.length - 1; i > 0; i--) {
                var j = Math.floor(Math.random() * (i + 1));
                var t = list[i];
                list[i] = list[j];
                list[j] = t;
            }
        } else if (r.order === 'Random') {
            list = urls.map(function () {
                return urls[Math.floor(Math.random() * urls.length)];
            });
        } else if (r.randomStart && list.length > 1) {
            list = randomStartRotate(list);
        }
        return list;
    }

    // Two decorative layers cross-fade; Multi Image runs through the list, Play once fades out after the last image, a single image stays unless Play
    // once without If Only 1 Image; the box is sized once by the first loaded image.
    function rotateArt(box, layerClass, urls, r, gen, size) {
        var list = r.multi ? artOrder(urls, r) : urls.slice(0, 1);
        var layers = [0, 1].map(function () {
            var img = document.createElement('img');
            img.className = layerClass;
            img.alt = '';
            img.style.opacity = '0';
            img.style.transition = 'opacity ' + r.fade + 'ms ease';
            img.style.objectPosition = r.position;
            box.appendChild(img);
            return img;
        });
        var visible = -1;
        var slide = 0;
        var failures = 0;
        var fades = 0;
        function next() {
            if (gen !== artGeneration || !box.isConnected) {
                return;
            }
            if (r.once && slide >= list.length) {
                if (visible >= 0) {
                    layers[visible].style.opacity = '0';
                }
                return;
            }
            var url = list[slide % list.length];
            var probe = new Image();
            probe.onload = function () {
                if (gen !== artGeneration || !box.isConnected) {
                    return;
                }
                failures = 0;
                if (size) {
                    size(probe.naturalWidth / probe.naturalHeight);
                    size = null;
                }
                var shown = visible === 0 ? 1 : 0;
                var layer = layers[shown];
                var fade = ++fades;
                layer.src = url;
                layer.style.zIndex = '2';
                layer.style.opacity = '0';
                void layer.offsetWidth;
                if (visible >= 0) {
                    layers[visible].style.zIndex = '1';
                    layers[visible].style.opacity = '0';
                }
                requestAnimationFrame(function () {
                    requestAnimationFrame(function () {
                        if (gen === artGeneration && fade === fades) {
                            layer.style.opacity = '1';
                        }
                    });
                });
                visible = shown;
                slide++;
                if ((r.multi && list.length > 1) || (list.length === 1 && r.once && !r.stay)) {
                    artTimer(next, r.cycle);
                }
            };
            probe.onerror = function () {
                if (gen === artGeneration) {
                    slide++;
                    if (++failures < 6) {
                        artTimer(next, 50);
                    }
                }
            };
            probe.src = url;
        }
        next();
    }

    function artLoads(url) {
        return new Promise(function (resolve) {
            var probe = new Image();
            probe.onload = function () {
                resolve(true);
            };
            probe.onerror = function () {
                resolve(false);
            };
            probe.src = url;
        });
    }

    var ART_FADE = 600;

    function artTag(api, id, type) {
        return Promise.resolve(api.getItem(api.getCurrentUserId(), id)).then(function (item) {
            return (item && item.ImageTags && item.ImageTags[type]) || '';
        }, function () {
            return '';
        });
    }

    function fadeOutBox(node) {
        node.style.transition = 'opacity ' + ART_FADE + 'ms ease';
        node.style.opacity = '0';
        setTimeout(function () {
            node.remove();
        }, ART_FADE + 50);
    }

    function fadeInBox(node) {
        node.style.opacity = '0';
        node.style.transition = 'opacity ' + ART_FADE + 'ms ease';
        void node.offsetWidth;
        requestAnimationFrame(function () {
            node.style.opacity = '1';
        });
    }

    // The LogoArt chain: Clearlogo = Jellyfin's own .detailLogo (sized by logoCss), Clearart = the game's Art image, Characterart = the list of
    // games/{id}/characterart, Hide = empty; a stage without image falls through. Jellyfin's logo is hidden by pbLogoArtHidden from the start
    // whenever the chain does not begin with it.
    // keep (a version picked on the standing page): nothing is torn down; the stage the new version's chain ends on is compared with view.pbSlot and
    // only a different picture is faded in over the old one.
    function startLogoArt(view, api, id, a, gen, characterArt, keep, current) {
        var logo = view.querySelector('.detailLogo');
        if (!logo || logoUntouched(a)) {
            return;
        }
        var chain = logoChain(a);
        var picked = keep || (!!view.pbFirst && view.pbFirst !== id.replace(/-/g, '').toLowerCase());
        var old = keep ? view.querySelector('.pbLogoArt') : null;
        var shown = keep ? view.pbSlot : null;
        var soft = keep && !old && !!shown && shown.kind === 'VanillaLogo';
        if ((chain[0] !== 'VanillaLogo' && !soft) || old) {
            logo.classList.add('pbLogoArtHidden');
        }
        function slot(kind) {
            if (!soft) {
                logo.classList.add('pbLogoArtHidden');
            }
            var box = document.createElement('div');
            box.className = 'pbLogoArt' + (kind === 'Clearart' ? ' pbLogoArtClearart' : '');
            box.style.setProperty('--pb-size', String(a.ArtLogoArtClearartSizePercent / 100));
            box.style.setProperty('--pb-offset', (kind === 'Clearart' ? a.ArtLogoArtClearartOffsetVw : a.ArtLogoArtCharacterartOffsetVw) + 'vw');
            box.style.setProperty('--pb-voffset', (kind === 'Clearart' ? a.ArtLogoArtClearartVerticalOffsetVh : 0) + 'vh');
            logo.parentNode.insertBefore(box, logo.nextSibling);
            return box;
        }
        function describe(i) {
            var kind = chain[i];
            if (!kind) {
                return Promise.resolve(null);
            }
            if (kind === 'Hide') {
                return Promise.resolve({ kind: kind });
            }
            if (kind === 'Characterart') {
                return characterArt().then(function (urls) {
                    return urls.length ? { kind: kind, urls: urls, sig: urls.map(function (u) { return u.split('/characterart/')[1]; }).join('|') } : describe(i + 1);
                });
            }
            var type = kind === 'Clearart' ? 'Art' : 'Logo';
            if (picked) {
                return artTag(api, id, type).then(function (tag) {
                    var tagged = tag ? api.getUrl('Items/' + id + '/Images/' + type, { tag: tag }) : null;
                    return (tagged ? artLoads(tagged) : Promise.resolve(false)).then(function (loads) {
                        return loads ? { kind: kind, url: tagged, sig: tag } : describe(i + 1);
                    });
                });
            }
            var url = api.getUrl('Items/' + id + '/Images/' + type);
            return artLoads(url).then(function (loads) {
                return loads ? { kind: kind, url: url, sig: undefined } : describe(i + 1);
            });
        }
        function sameShown(d) {
            if (!shown || shown.kind !== d.kind) {
                return Promise.resolve(false);
            }
            if (d.kind === 'Characterart' || d.kind === 'Hide') {
                return Promise.resolve(shown.sig === d.sig);
            }
            var type = d.kind === 'Clearart' ? 'Art' : 'Logo';
            return Promise.resolve(shown.sig !== undefined ? shown.sig : artTag(api, shown.id, type)).then(function (before) {
                return !d.sig || before === d.sig;
            });
        }
        function render(d) {
            var box = null;
            var state = { kind: d.kind, id: id, sig: d.sig };
            if (d.kind === 'Hide') {
                logo.classList.add('pbLogoArtHidden');
            } else if (d.kind === 'VanillaLogo') {
                if (old) {
                    logo.style.opacity = '0';
                }
                logo.classList.remove('pbLogoArtHidden');
                if (old) {
                    fadeInBox(logo);
                    setTimeout(function () {
                        if (view.pbSlot === state) {
                            logo.style.opacity = '';
                            logo.style.transition = '';
                        }
                    }, ART_FADE + 80);
                }
            } else if (d.kind === 'Characterart') {
                box = slot(d.kind);
                rotateArt(box, 'pbLogoArtLayer', d.urls, artRotation(a, 'ArtLogoArt', a.ArtLogoArtCharacterartHorizontalAlign), gen, function (ratio) {
                    sizeArtBox(box, a, 'ArtLogoArtCharacterart', ratio);
                });
            } else {
                var img = document.createElement('img');
                img.className = 'pbLogoArtLayer';
                img.alt = '';
                img.src = d.url;
                box = slot(d.kind);
                box.appendChild(img);
            }
            if (old) {
                if (box) {
                    fadeInBox(box);
                }
                fadeOutBox(old);
            } else if (soft && box) {
                fadeInBox(box);
                logo.style.transition = 'opacity ' + ART_FADE + 'ms ease';
                logo.style.opacity = '0';
                setTimeout(function () {
                    if (view.pbSlot === state) {
                        logo.classList.add('pbLogoArtHidden');
                        logo.style.opacity = '';
                        logo.style.transition = '';
                    } else if (!view.pbSlot || view.pbSlot.kind !== 'VanillaLogo') {
                        logo.style.opacity = '';
                        logo.style.transition = '';
                    }
                }, ART_FADE + 50);
            }
            view.pbSlot = state;
        }
        describe(0).then(function (d) {
            if (gen !== artGeneration || !current()) {
                return;
            }
            if (!d) {
                if (old) {
                    fadeOutBox(old);
                    view.pbSlot = null;
                }
                return;
            }
            if (!keep) {
                render(d);
                return;
            }
            sameShown(d).then(function (same) {
                if (gen !== artGeneration || !current()) {
                    return;
                }
                if (!same) {
                    render(d);
                    return;
                }
                shown.id = id;
                if (d.kind === 'VanillaLogo') {
                    logo.classList.remove('pbLogoArtHidden');
                } else if (d.kind !== 'Hide' && old) {
                    logo.classList.add('pbLogoArtHidden');
                }
            });
        });
    }

    function startCharacterArt(view, a, gen, characterArt, keep, current) {
        if (!a.ArtCharacterArtShow) {
            return;
        }
        var position = a.ArtCharacterArtPosition;
        var prefix = 'ArtCharacterArt' + position;
        characterArt().then(function (urls) {
            var logo = view.querySelector('.detailLogo');
            var top = position === 'TopLeft' || position === 'TopRight';
            // a picked version without characterart (and no first version's list in its place, startArt): the box fades out
            if (gen === artGeneration && current() && keep && !urls.length && view.pbCorner && view.pbCorner.isConnected) {
                fadeOutBox(view.pbCorner.parentNode.classList.contains('pbCharArtAnchor') ? view.pbCorner.parentNode : view.pbCorner);
                view.pbCorner = null;
            }
            if (gen !== artGeneration || !current() || !urls.length || (top && !logo) || (a.ArtCharacterArtHideWithoutLogo && !logo)) {
                return;
            }
            // a version picked on the standing page: the box stays unless this version has other characterart; then it fades over the old one
            var sig = urls.map(function (u) { return u.split('/characterart/')[1]; }).join('|');
            var before = keep && view.pbCorner && view.pbCorner.isConnected ? view.pbCorner : null;
            if (before && before.pbSig === sig) {
                return;
            }
            var place = 'pbCharArt-' + position.replace(/([a-z])([A-Z])/g, '$1-$2').toLowerCase();
            var box = document.createElement('div');
            box.className = 'pbCharArtBox ' + place;
            box.pbSig = sig;
            view.pbCorner = box;
            box.style.setProperty('--pb-offset', a[prefix + 'OffsetVw'] + 'vw');
            box.style.setProperty('--pb-fs-offset', a[prefix + 'FullscreenOffsetVw'] + 'vw');
            if (top) {
                var anchor = document.createElement('div');
                anchor.className = 'pbCharArtAnchor ' + place;
                anchor.appendChild(box);
                logo.parentNode.insertBefore(anchor, logo);
            } else {
                document.body.appendChild(box);
            }
            artFrame();
            if (before) {
                fadeInBox(box);
                fadeOutBox(before.parentNode.classList.contains('pbCharArtAnchor') ? before.parentNode : before);
            }
            var r = artRotation(a, 'ArtCharacterArt', a[prefix + 'HorizontalAlign']);
            var start = function () {
                rotateArt(box, 'pbCharArtLayer', urls, r, gen, function (ratio) {
                    sizeArtBox(box, a, prefix, ratio);
                });
            };
            if (r.multi && a.ArtCharacterArtDelayEnabled) {
                artTimer(start, a.ArtCharacterArtDelayMs);
            } else {
                start();
            }
        });
    }

    // Fullscreen: the Fullscreen API, or a window as high as the screen (browser fullscreen key, which the API does not report). The page's left edge for the bottom
    // left box and the top right anchor (a permanent drawer shifts the page).
    function artFrame() {
        var boxes = document.querySelectorAll('.pbCharArtBox, .pbCharArtAnchor');
        if (!boxes.length) {
            return;
        }
        var fullscreen = !!(document.fullscreenElement || document.webkitFullscreenElement || document.mozFullScreenElement)
            || (!!window.screen && window.innerHeight >= window.screen.height);
        var page = document.querySelector('.mainAnimatedPage:not(.hide)');
        var left = (page ? Math.max(0, Math.round(page.getBoundingClientRect().left)) : 0) + 'px';
        [].forEach.call(boxes, function (box) {
            if (box.classList.contains('pbCharArtBox')) {
                box.classList.toggle('pbCharArtFullscreen', fullscreen);
            }
            box.style.setProperty('--pb-page-left', left);
        });
    }

    // rule: the CharacterArt row of Artwork on Version Change ({ follow, keep }), only for a version picked on the standing page (keep).
    function startArt(view, api, id, a, gen, keep, current, rule) {
        setArtStyle('pbArtPosterStyle', posterCss(a.ArtPosterSizePercent, a.ArtPosterAnchor));
        setArtStyle('pbArtStyle', logoUntouched(a) && !a.ArtCharacterArtShow ? '' : ART_CSS + logoCss(a));
        var listOf = function (game) {
            return api.getJSON(api.getUrl('PlayniteBridge/games/' + encodeURIComponent(game) + '/characterart')).then(function (entries) {
                return (entries || []).map(function (entry) {
                    return api.getUrl('PlayniteBridge/games/' + encodeURIComponent(game) + '/characterart/' + entry.Index, { tag: entry.Tag });
                });
            }, function () {
                return [];
            });
        };
        // A picked version shows the list of the version the page was opened with while its Follows Version is off, or while Keep When Version Has
        // None is on and it has none.
        var first = rule && view.pbFirst && view.pbFirst !== id.replace(/-/g, '').toLowerCase() ? view.pbFirst : null;
        var list = null;
        var characterArt = function () {
            if (!list) {
                list = first && !rule.follow ? listOf(first) : listOf(id).then(function (urls) {
                    return first && rule.keep && !urls.length ? listOf(first) : urls;
                });
            }
            return list;
        };
        startLogoArt(view, api, id, a, gen, characterArt, keep, current);
        startCharacterArt(view, a, gen, characterArt, keep, current);
    }

    // The answer for the art: the cached one only while it is younger than 30 s, else a new request, so a change saved on the admin page shows up on
    // the next game page.
    function artConfiguration(api) {
        var stale = !configPromise || Date.now() - configTime > 30000;
        var cached = clientConfiguration(api);
        return stale ? configPromise : cached;
    }

    var artHash = null;

    document.addEventListener('viewshow', function (e) {
        var view = e.target;
        // A version picked on the standing page (switchInPlace): nothing is torn down, the art of the game stays and only a different
        // Art image is swapped (startLogoArt); a newer pick makes the older answers void (pbArtSeq).
        var keep = !!(view && view.pbArtKeep && view.id === 'itemDetailPage');
        if (view) {
            view.pbArtKeep = false;
            view.pbArtSeq = (view.pbArtSeq || 0) + 1;
        }
        var seq = view && view.pbArtSeq;
        var current = function () {
            return view.pbArtSeq === seq;
        };
        if (!keep) {
            if (view) {
                view.pbSlot = null;
                view.pbCorner = null;
            }
            stopArt();
            // Our two style elements stay across page changes only while an answer younger than 30 s says ArtworkPlus is not active; else they go at
            // once.
            if (!(lastConfig && Date.now() - lastConfigTime < 30000 && !apDraws(lastConfig))) {
                setArtStyle('pbArtPosterStyle', '');
                setArtStyle('pbArtStyle', '');
            }
        }
        var id = itemId();
        var api = window.ApiClient;
        if (!view || view.id !== 'itemDetailPage' || !id || !api) {
            return;
        }
        artHash = window.location.hash;
        var gen = artGeneration;
        // A cached answer without ArtworkPlus whose chain does not begin with Jellyfin's logo hides that logo as soon as the page is known as a game,
        // before the fresh answer. Only an answer younger than 30 s counts.
        var early = lastConfig && Date.now() - lastConfigTime < 30000 && !apDraws(lastConfig) && lastConfig.Artwork;
        if (!keep && early && !logoUntouched(early) && logoChain(early)[0] !== 'VanillaLogo') {
            loadGame(api, id).then(function (game) {
                var logo = view.querySelector('.detailLogo');
                if (game && gen === artGeneration && current() && logo) {
                    setArtStyle('pbArtStyle', ART_CSS + logoCss(early));
                    logo.classList.add('pbLogoArtHidden');
                }
            });
        }
        Promise.all([loadGame(api, id), artConfiguration(api)]).then(function (results) {
            var config = results[1];
            if (gen !== artGeneration || !current() || !results[0]) {
                return;
            }
            var hidden = view.querySelector('.detailLogo.pbLogoArtHidden');
            if (hidden && !keep) {
                hidden.classList.remove('pbLogoArtHidden');
            }
            if (!config || apDraws(config) || !config.Artwork) {
                if (keep) {
                    stopArt();
                }
                setArtStyle('pbArtPosterStyle', '');
                setArtStyle('pbArtStyle', '');
                return;
            }
            startArt(view, api, id, config.Artwork, gen, keep, current, config.VersionArtwork && config.VersionArtwork.Characterart);
        }, function () {
            var hidden = gen === artGeneration && current() && !keep && view.querySelector('.detailLogo.pbLogoArtHidden');
            if (hidden) {
                hidden.classList.remove('pbLogoArtHidden');
            }
        });
    });
    window.addEventListener('hashchange', function () {
        if (window.location.hash !== artHash) {
            stopArt();
        }
    });
    window.addEventListener('resize', artFrame);
    document.addEventListener('fullscreenchange', artFrame);

    // Backdrops on game pages and the games library page (ArtworkPlus's Detail View and Library View for games, mirrored here). Only with an Artwork
    // answer younger than 30 s and its Show on; Artwork is null while ArtworkPlus is active. A game after a game or after the games library
    // cross-fades in the same container; every other page releases at once, #/video only pauses.
    var BD_FADE = 800;
    var BD_CSS = 'body.pbBackdropOn .backdropContainer{visibility:hidden !important}';
    // The Ken Burns constants of the Detail View and the Library View.
    var BD_KEN_BURNS = {
        Detail: { from: 1.10, to: 1.30, pan: 15, easing: 'cubic-bezier(0.645, 0.045, 0.355, 1)' },
        Library: { from: 1, to: 1.12, pan: 30, easing: 'ease-in-out' },
        Collection: { from: 1.10, to: 1.30, pan: 15, easing: 'cubic-bezier(0.645, 0.045, 0.355, 1)' }
    };
    var bdGeneration = 0;
    var bdTimer = null;
    var bdKey = null;
    var bdHash = null;
    var bdRun = null;
    var bdContainer = null;
    var bdFrame = null;
    var bdFrameKey = null;
    var bdObserver = null;
    var bdBackground = null;
    var bdSig = null;      // the values and the URLs the running engine was started with
    var bdPick = 0;        // +1 at every version pick on the standing page: older answers apply nothing
    var bdOwnWithBackdrop = false;

    // The values of one view, or null while its Show is off; smaller numbers count as the lower limits. A collection page has no Order setting: the
    // server composes the list in its final order, only Mixed is reshuffled by the page at every round.
    function bdValues(a, view) {
        var p = 'ArtBackdrops' + view;
        var collection = view === 'Collection';
        return a && a[p + 'Show'] ? {
            view: view,
            cycle: Math.max(a[p + 'CycleTimeMs'], Math.ceil(BD_FADE * 1.5)),
            kenBurns: a[p + 'KenBurnsEnabled'],
            zoom: Math.max(a[p + 'KenBurnsZoomMs'], 500),
            pan: Math.max(a[p + 'KenBurnsPanMs'], 500),
            order: collection ? (a[p + 'Source'] === 'Mixed' ? 'Shuffle' : 'Sequential') : a[p + 'OrderMode'],
            randomStart: collection ? false : a[p + 'RandomStart']
        } : null;
    }

    // Rotation: Sequential +1 with wrap, Shuffle a Fisher-Yates bag per round whose first image is never the last of the round before, Random an
    // independent draw; Random Start (Sequential only) rotates the list so that any image may come first.
    function bdEngine(urls, order, randomStart) {
        var list = urls.slice();
        if (order === 'Sequential' && randomStart && list.length > 1) {
            list = randomStartRotate(list);
        }
        var index = -1;
        var bag = [];
        return {
            list: list,
            next: function () {
                if (order === 'Shuffle') {
                    if (!bag.length) {
                        bag = list.map(function (url, i) {
                            return i;
                        });
                        for (var i = bag.length - 1; i > 0; i--) {
                            var j = Math.floor(Math.random() * (i + 1));
                            var t = bag[i];
                            bag[i] = bag[j];
                            bag[j] = t;
                        }
                        if (bag.length > 1 && bag[0] === index) {
                            var swap = 1 + Math.floor(Math.random() * (bag.length - 1));
                            bag[0] = bag[swap];
                            bag[swap] = index;
                        }
                    }
                    index = bag.shift();
                } else if (order === 'Random') {
                    index = Math.floor(Math.random() * list.length);
                } else {
                    index = (index + 1) % list.length;
                }
                return list[index];
            },
            peek: function () {
                return order === 'Shuffle' ? (bag.length ? list[bag[0]] : null) : order === 'Random' ? null : list[(index + 1) % list.length];
            },
            remove: function (url) {
                var i = list.indexOf(url);
                if (i === -1) {
                    return;
                }
                list.splice(i, 1);
                if (i <= index) {
                    index--;
                }
                bag = bag.filter(function (v) {
                    return v !== i;
                }).map(function (v) {
                    return v > i ? v - 1 : v;
                });
            }
        };
    }

    function bdVideo() {
        return window.location.hash.split('?')[0].replace(/^#\/?/, '') === 'video';
    }

    // The claim: vanilla's container hidden by #pbBackdropStyle, .backgroundContainer kept on withBackdrop (else the theme paints the page
    // opaque) unless playback made it transparent; Jellyfin's own clearBackdrop may take the class off again, the observer puts it back.
    function bdClaim() {
        document.body.classList.add('pbBackdropOn');
        var bg = document.querySelector('.backgroundContainer');
        if (!bg || bdObserver) {
            return;
        }
        var keep = function () {
            if (!bg.classList.contains('backgroundContainer-transparent') && !bg.classList.contains('withBackdrop')) {
                bg.classList.add('withBackdrop');
                bdOwnWithBackdrop = true;
            }
        };
        keep();
        bdBackground = bg;
        bdObserver = new MutationObserver(keep);
        bdObserver.observe(bg, { attributes: true, attributeFilter: ['class'] });
    }

    function bdRelease() {
        bdPick++;
        bdGeneration++;
        bdKey = null;
        bdRun = null;
        clearTimeout(bdTimer);
        bdTimer = null;
        if (document.body.classList.contains('pbBackdropOn')) {
            document.body.classList.remove('pbBackdropOn');
        }
        if (bdObserver) {
            bdObserver.disconnect();
            bdObserver = null;
        }
        if (bdOwnWithBackdrop && bdBackground && !document.querySelector('.backdropContainer .backdropImage')) {
            bdBackground.classList.remove('withBackdrop');
        }
        bdOwnWithBackdrop = false;
        bdBackground = null;
        var container = bdContainer;
        bdContainer = null;
        bdFrame = null;
        bdFrameKey = null;
        if (container) {
            container.style.transition = 'opacity ' + BD_FADE + 'ms ease';
            container.style.opacity = '0';
            setTimeout(function () {
                container.remove();
            }, BD_FADE);
        }
    }

    function bdPause(paused) {
        if (paused) {
            bdPick++;
        }
        if (bdRun) {
            bdRun.paused = paused;
            clearTimeout(bdTimer);
            bdTimer = null;
            if (!paused) {
                bdSchedule(bdRun);
            }
        }
    }

    function bdSchedule(run) {
        clearTimeout(bdTimer);
        bdTimer = null;
        if (!run.paused && run.shownAt && run.engine.list.length > 1) {
            bdTimer = setTimeout(bdShowNext, Math.max(0, run.cycle - (Date.now() - run.shownAt)));
        }
    }

    // The own container (first child of <body>, behind the page like Jellyfin's .backdropContainer) and, with Ken Burns, its frame; another
    // view, zoom or pan gets a new frame on top, the old one goes with its last layer.
    function bdTarget(run) {
        if (!bdContainer) {
            bdContainer = document.createElement('div');
            bdContainer.className = 'pbBackdrop';
            bdContainer.style.cssText = 'position:fixed;inset:0;z-index:-1;contain:layout style size';
            document.body.insertBefore(bdContainer, document.body.firstChild);
        }
        if (!run.kenBurns) {
            bdFrame = null;
            bdFrameKey = null;
            return bdContainer;
        }
        var key = run.view + '|' + run.zoom + '|' + run.pan;
        if (bdFrame && bdFrameKey === key) {
            return bdFrame;
        }
        var kb = BD_KEN_BURNS[run.view];
        var zoom = document.createElement('div');
        zoom.className = 'pbKenBurnsZoom';
        zoom.style.cssText = 'position:absolute;inset:0;overflow:hidden';
        var pan = document.createElement('div');
        pan.className = 'pbKenBurnsPan';
        pan.style.cssText = 'position:absolute;inset:0';
        zoom.appendChild(pan);
        bdContainer.appendChild(zoom);
        zoom.animate([{ transform: 'scale(' + kb.from + ')' }, { transform: 'scale(' + kb.to + ')' }],
            { duration: run.zoom, easing: kb.easing, iterations: Infinity, direction: 'alternate' }).currentTime = Math.random() * run.zoom;
        pan.animate([{ transform: 'translate(-' + kb.pan + 'px, -' + kb.pan + 'px)' }, { transform: 'translate(' + kb.pan + 'px, ' + kb.pan + 'px)' }],
            { duration: run.pan, easing: kb.easing, iterations: Infinity, direction: 'alternate' }).currentTime = Math.random() * run.pan;
        bdFrame = pan;
        bdFrameKey = key;
        return pan;
    }

    function bdRemoveLayer(layer) {
        var parent = layer.parentNode;
        if (!parent) {
            return;
        }
        layer.remove();
        if (parent !== bdFrame && parent.classList.contains('pbKenBurnsPan') && !parent.firstChild) {
            parent.parentNode.remove();
        }
    }

    // One image as a new layer (Jellyfin's .backdropImage) fading in over the old one, which is removed when the fade ends; the first image of
    // a visit waits for 20 calm frames (at most 1500 ms). An image that does not load leaves the list; with none left the page is released.
    function bdShowNext() {
        var run = bdRun;
        var gen = bdGeneration;
        var url = run.engine.next();
        var probe = new Image();
        probe.onload = function () {
            if (gen !== bdGeneration) {
                return;
            }
            var frame = bdTarget(run);
            var old = bdContainer.querySelector('.pbBackdropShowing');
            var layer = document.createElement('div');
            layer.className = 'backdropImage pbBackdropLayer pbBackdropShowing';
            layer.style.opacity = '0';
            layer.style.transition = 'opacity ' + BD_FADE + 'ms ease';
            layer.style.backgroundImage = "url('" + url.replace(/'/g, '%27') + "')";
            if (old) {
                old.classList.remove('pbBackdropShowing');
            }
            frame.appendChild(layer);
            var begin = function () {
                if (gen !== bdGeneration) {
                    return;
                }
                void layer.offsetWidth;
                layer.style.opacity = '1';
                run.shownAt = Date.now();
                bdSchedule(run);
                if (run.view === 'Library') {
                    (window.requestIdleCallback || function (fn) {
                        setTimeout(fn, 1500);
                    })(function () {
                        var next = gen === bdGeneration && run.engine.peek();
                        if (next) {
                            new Image().src = next;
                        }
                    });
                }
                if (old) {
                    var done = false;
                    var cleanup = function (e) {
                        if (!done && !(e && e.propertyName !== 'opacity')) {
                            done = true;
                            layer.removeEventListener('transitionend', cleanup);
                            bdRemoveLayer(old);
                        }
                    };
                    layer.addEventListener('transitionend', cleanup);
                    setTimeout(cleanup, BD_FADE + 1000);
                }
            };
            if (!run.shown) {
                run.shown = true;
                var start = performance.now();
                var prev = null;
                var calm = 0;
                var step = function (ts) {
                    if (gen !== bdGeneration) {
                        return;
                    }
                    calm = prev !== null && ts - prev <= 34 ? calm + 1 : 0;
                    prev = ts;
                    if (calm >= 20 || performance.now() - start >= 1500) {
                        begin();
                    } else {
                        requestAnimationFrame(step);
                    }
                };
                requestAnimationFrame(step);
            } else {
                requestAnimationFrame(function () {
                    requestAnimationFrame(begin);
                });
            }
        };
        probe.onerror = function () {
            if (gen !== bdGeneration) {
                return;
            }
            run.engine.remove(url);
            if (run.engine.list.length) {
                bdShowNext();
            } else {
                bdRelease();
            }
        };
        probe.src = url;
    }

    // A visit with its image list: an empty list claims nothing (vanilla stays untouched); else the claim and the first image. The Detail View
    // preloads every image at low priority, the Library View only the next one when idle.
    // The identity of a backdrop set across versions: the values and the pictures (tag and index), not the item id in the URL.
    function bdSigOf(values, urls) {
        return JSON.stringify([values, urls.map(function (url) {
            return url.replace(/Items\/[0-9a-f]{32}\//i, 'Items/_/');
        })]);
    }

    function bdStart(gen, values, urls) {
        if (gen !== bdGeneration) {
            return;
        }
        bdSig = bdSigOf(values, urls);
        if (!urls.length) {
            bdRelease();
            return;
        }
        bdRun = Object.assign({ engine: bdEngine(urls, values.order, values.randomStart), shownAt: 0, shown: false, paused: false }, values);
        bdClaim();
        if (values.view === 'Detail' || values.view === 'Collection') {
            urls.slice(1, values.view === 'Collection' ? 9 : urls.length).forEach(function (url) {
                var image = new Image();
                image.fetchPriority = 'low';
                image.src = url;
            });
        }
        bdShowNext();
    }

    function bdCollectionKnown(id, config) {
        var c = config || lastConfig;
        return !!(c && (c.CollectionIds || []).indexOf(id.replace(/-/g, '').toLowerCase()) !== -1);
    }

    function bdArtwork(config) {
        var a = config && !apDraws(config) && config.Artwork || null;
        setArtStyle('pbBackdropStyle', a && (a.ArtBackdropsDetailShow || a.ArtBackdropsLibraryShow || a.ArtBackdropsCollectionShow) ? BD_CSS : '');
        return a;
    }

    document.addEventListener('viewshow', function (e) {
        var view = e.target;
        var api = window.ApiClient;
        if (bdVideo()) {
            bdPause(true);
            return;
        }
        var id = view && view.id === 'itemDetailPage' ? itemId() : null;
        // A collection of the games library opens as a list page (#/list.html?parentId=<collection>); the same engine draws its backdrops. Before the
        // first answer nothing is known of the id, so it passes and the check below decides.
        if (!id && view && view.classList && view.classList.contains('libraryPage')) {
            var listed = routeParam('list.html', 'parentId');
            id = listed && (!lastConfig || bdCollectionKnown(listed)) ? listed : null;
        }
        var library = view && view.id === 'moviesPage' ? routeParam('movies.html', 'topParentId') : null;
        if (!api || !(id || library)) {
            bdRelease();
            return;
        }
        var key = (id ? 'Detail|' + id : 'Library|' + library).replace(/-/g, '').toLowerCase();
        if (id && notGames[id.replace(/-/g, '').toLowerCase()] && !bdCollectionKnown(id)) {
            bdRelease();
            return;
        }
        bdHash = window.location.hash;
        if (bdRun && bdRun.paused && bdKey === key) {
            bdPause(false);
            return;
        }
        // the claim stays until the answers are known (game -> game cross-fades in the same container); a non-game, Show off, an empty list or an
        // error ends in bdRelease, which drops it
        // A version picked on the standing page: its backdrops are compared with the running engine's; the same set keeps the rotation where it is, a
        // different one restarts it (cross-fade).
        if (id && e.detail && e.detail.pbInPlace && bdRun && !bdRun.paused && bdSig && bdKey && bdKey.indexOf('detail|') === 0) {
            var pick = ++bdPick;
            Promise.all([loadGame(api, id), artConfiguration(api)]).then(function (results) {
                var values = pick === bdPick && results[0] && bdValues(bdArtwork(results[1]), 'Detail');
                if (!values) {
                    return pick === bdPick && bdBegin();
                }
                return api.getItem(api.getCurrentUserId(), id).then(function (item) {
                    if (pick !== bdPick) {
                        return;
                    }
                    var urls = (item.BackdropImageTags || []).map(function (tag, i) {
                        return api.getUrl('Items/' + item.Id + '/Images/Backdrop/' + i, { tag: tag });
                    });
                    if (bdSigOf(values, urls) === bdSig) {
                        bdKey = key;
                    } else {
                        bdBegin();
                    }
                });
            }).catch(function () {
                if (pick === bdPick) {
                    bdBegin();
                }
            });
            return;
        }
        bdBegin();
        function bdBegin() {
        bdPick++;
        bdKey = key;
        bdGeneration++;
        clearTimeout(bdTimer);
        bdTimer = null;
        var gen = bdGeneration;
        var failed = function () {
            if (gen === bdGeneration) {
                bdArtwork(null);
                bdRelease();
            }
        };
        if (id) {
            // vanilla parity (itemDetails/index.js renderBackdrop): no detail backdrop in the mobile layout or below 1000 px, read once here
            var skip = document.documentElement.classList.contains('layout-mobile') || window.innerWidth < 1000;
            var known = bdCollectionKnown(id);
            Promise.all([known ? null : loadGame(api, id), artConfiguration(api)]).then(function (results) {
                if (gen !== bdGeneration) {
                    return;
                }
                // A collection of the games library (ClientConfiguration CollectionIds): its own list, composed by the server for this visit
                if (known || (!results[0] && bdCollectionKnown(id, results[1]))) {
                    var collectionValues = bdValues(bdArtwork(results[1]), 'Collection');
                    if (!collectionValues || skip) {
                        bdRelease();
                        return;
                    }
                    var plain = id.replace(/-/g, '').toLowerCase();
                    bdKey = 'collection|' + plain;
                    return api.ajax({ type: 'GET', url: api.getUrl('PlayniteBridge/collections/' + plain + '/backdrops') }).then(function (response) {
                        return response && response.status === 200 ? response.json() : [];
                    }).then(function (entries) {
                        bdStart(gen, collectionValues, entries.map(function (entry) {
                            return api.getUrl('Items/' + plain + '/Images/Backdrop/' + entry.index, { tag: entry.tag });
                        }));
                    });
                }
                var values = bdValues(bdArtwork(results[1]), 'Detail');
                if (!results[0] || !values || skip) {
                    bdRelease();
                    return;
                }
                return api.getItem(api.getCurrentUserId(), id).then(function (item) {
                    bdStart(gen, values, (item.BackdropImageTags || []).map(function (tag, i) {
                        return api.getUrl('Items/' + item.Id + '/Images/Backdrop/' + i, { tag: tag });
                    }));
                });
            }).catch(failed);
        } else {
            artConfiguration(api).then(function (config) {
                if (gen !== bdGeneration) {
                    return;
                }
                var values = bdValues(bdArtwork(config), 'Library');
                // only the games library; like ArtworkPlus's Library View it paints whatever Jellyfin's "Backdrops" display setting says, so its own
                // box alone decides
                if (!values || key !== 'library|' + config.LibraryId) {
                    bdRelease();
                    return;
                }
                return api.getItems(api.getCurrentUserId(), {
                    SortBy: 'IsFavoriteOrLiked,Random', Limit: 20, Recursive: true, IncludeItemTypes: 'movie', ImageTypes: 'Backdrop',
                    ParentId: config.LibraryId, EnableTotalRecordCount: false
                }).then(function (result) {
                    bdStart(gen, values, result.Items.filter(function (item) {
                        return item.BackdropImageTags && item.BackdropImageTags.length;
                    }).map(function (item) {
                        return api.getScaledImageUrl(item.Id, { type: 'Backdrop', tag: item.BackdropImageTags[0], index: 0, maxWidth: window.innerWidth });
                    }));
                });
            }).catch(failed);
        }
        }
    });
    // A hashchange of the navigation the last viewshow started for keeps the backdrops; #/video pauses; a game page or the games library
    // waits for its viewshow (cross-fade), every other page releases at once.
    window.addEventListener('hashchange', function () {
        var hash = window.location.hash;
        if (hash === bdHash) {
            return;
        }
        if (bdVideo()) {
            bdPause(true);
            return;
        }
        var library = routeParam('movies.html', 'topParentId');
        if (!itemId() && !(library && lastConfig && library.replace(/-/g, '').toLowerCase() === lastConfig.LibraryId)) {
            bdRelease();
        }
    });

    // Player artwork: while a game's trailer or extra plays in Jellyfin's video player (its id in extraIds), the 16 layers of VideoOSD Tweaks &
    // Candy's Artwork-Display game profile are rebuilt on its rules under own names, so the same values give the same pixels. Only with PlayerArtwork
    // in an answer younger than 30 s, and only while html[data-jvosd-artwork] is missing (VideoOSD's addon sets it while it draws; then ours go at
    // once and return after it is gone). Everything runs only while #videoOsdPage is shown; leaving it removes every own element, listener, observer,
    // timer and frame.
    // The layers in VideoOSD's element order, which decides between equal z-index values: [key, property part, image type, backdrop index]
    var PA_LAYERS = [['logo', 'Logo', 'Logo'], ['clearart', 'Clearart', 'Art'], ['disc', 'Disc', 'Disc'], ['banner', 'Banner', 'Banner'],
        ['poster', 'Poster', 'Primary'], ['thumb', 'Thumb', 'Thumb'], ['backdrop', 'Backdrop', 'Backdrop', 0]].concat([1, 2, 3, 4, 5, 6, 7, 8, 9].map(function (i) {
        return ['backdrop' + i, 'Backdrop' + i, 'Backdrop', i];
    })).concat([['characterart', 'CharacterArt', null, undefined, 'characterart'], ['boxfront', 'BoxFront', null, undefined, 'boxfront'], ['boxback', 'BoxBack', null, undefined, 'boxback'],
        ['boxspine', 'BoxSpine', null, undefined, 'boxspine'], ['box3d', 'Box3D', null, undefined, 'box3d'], ['square', 'Square', null, undefined, 'square'],
        ['icon', 'Icon', null, undefined, 'icon']]);
    // VideoOSD's header height (3.5em + 2 x 0.8em) and minimum free band (a smaller band hides every layer).
    var PA_HEADER_REM = 5.1;
    var PA_MIN_BAND_REM = 4;
    // VideoOSD's two style elements in one: layers hidden while the band is too small and below 50em, and a z-index fix with the value 10 (vanilla
    // .osdHeader has z-index 1 only, the layers have 1-9).
    var PA_CSS = '#pbPlayerArt[data-pb-tight] .pbPlayerArtLayer{visibility:hidden !important}' +
        '@media all and (max-width:50em){.pbPlayerArtLayer{visibility:hidden !important}}' +
        'body.pbPlayerArtOn .videoOsdBottom,body.pbPlayerArtOn .skinHeader,body.pbPlayerArtOn .playerStats,body.pbPlayerArtOn .sliderBubbleTrack,' +
        'body.pbPlayerArtOn .upNextContainer,body.pbPlayerArtOn .upNextDialog{z-index:10 !important}';
    var paOn = false;          // listeners and observers are attached (#videoOsdPage shown with PlayerArtwork)
    var paSession = 0;         // +1 at every start and stop: a late ClientConfiguration answer of an older viewshow attaches nothing
    var paGen = 0;             // +1 at every item change, reset and hashchange: a resolve or image load of an older number applies nothing
    var paItem = null;         // the playing item the layers were resolved for
    var paValues = null;       // the drawn values (paRead), null while nothing is drawn
    var paRoot = null;         // div#pbPlayerArt
    var paImgs = {};           // key -> img of the enabled layers
    var paExtra = [];          // further images of a layer: the stacked discs and the second characterart image, { img, key, shift }
    var paCaTimer = null;      // the characterart rotation
    var paDiscItem = null;     // the id of the item whose disc the main disc layer shows
    var paClearart = 'none';   // none | real | fallback
    var paAngle = 0;
    var paLast = null;         // time of the last disc frame, null while paused
    var paFrame = null;        // requestAnimationFrame of the disc
    var paObserver = null;
    var paResize = null;
    var paObservedCtrl = null;
    var paDpr = null;
    var paTimer = null;
    var paStale = true;        // the anchors may be out of date (bar hidden at the last measure)
    var paBarShown = false;
    var paVars = {};           // the last measured anchors in px: top, bottom, left, right, band

    function paRead(c) {
        var layers = {};
        PA_LAYERS.forEach(function (l) {
            var p = 'PlayerArt' + l[1];
            layers[l[0]] = {
                on: c[p + 'Enabled'] === true, source: c[p + 'Source'], h: String(c[p + 'Horizontal']).toLowerCase(), v: String(c[p + 'Vertical']).toLowerCase(),
                left: c[p + 'OffsetLeftRem'], right: c[p + 'OffsetRightRem'], top: c[p + 'OffsetTopRem'], bottom: c[p + 'OffsetBottomRem'],
                maxH: c[p + 'MaxHeightRem'], maxW: c[p + 'MaxWidthRem'], z: c[p + 'ZIndex']
            };
        });
        var whole = function (value, low, high, standard) {
            var n = Number(value);
            return isFinite(n) ? Math.min(high, Math.max(low, Math.round(n))) : standard;
        };
        return {
            discMulti: c.PlayerArtDiscShowMultiple === true, discMax: whole(c.PlayerArtDiscMaxCount, 1, 20, 6),
            caMulti: c.PlayerArtCharacterArtMultiImage === true, caCycle: whole(c.PlayerArtCharacterArtCycleTimeMs, 1000, 3600000, 5000),
            caFade: whole(c.PlayerArtCharacterArtFadeTimeMs, 0, 3600000, 1000), caShuffle: c.PlayerArtCharacterArtOrderMode !== 'Sequential',
            caRandomStart: c.PlayerArtCharacterArtRandomStart === true,
            layers: layers, fallback: c.PlayerArtClearartFallbackToLogo === true, spin: c.PlayerArtDiscSpinning === true,
            seconds: Math.max(1, Number(c.PlayerArtDiscRotationSeconds) || 1),
            hideFallback: c.PlayerArtHideLogoWhenClearartFallback === true, hideAvailable: c.PlayerArtHideLogoWhenClearartAvailable === true
        };
    }

    function paHideLogo() {
        return paClearart === 'real' && paValues.hideAvailable || paClearart === 'fallback' && paValues.hideFallback;
    }

    // Position and size caps of one layer from the anchors; size = the layer whose greatest sizes count (the clearlogo for a clearart that falls back
    // to it). A greatest width of 0 is Auto: only the cap of the free space.
    function paApply(img, cfg, size) {
        var s = size || cfg;
        var capH, capW;
        if (cfg.v === 'top') {
            img.style.top = 'calc(var(--pb-osd-top, 0px) + ' + cfg.top + 'rem)';
            img.style.bottom = 'auto';
            capH = 'calc(var(--pb-osd-band, 100vh) - (' + cfg.top + 'rem))';
        } else if (cfg.v === 'center') {
            img.style.top = 'calc(var(--pb-osd-top, 0px) + var(--pb-osd-band, 100vh) / 2)';
            img.style.bottom = 'auto';
            capH = 'var(--pb-osd-band, 100vh)';
        } else {
            img.style.top = 'auto';
            img.style.bottom = 'calc(var(--pb-osd-bottom, 0px) + ' + cfg.bottom + 'rem)';
            capH = 'calc(var(--pb-osd-band, 100vh) - (' + cfg.bottom + 'rem))';
        }
        if (cfg.h === 'left') {
            img.style.left = 'calc(var(--pb-osd-left, 0px) + ' + cfg.left + 'rem)';
            img.style.right = 'auto';
            capW = 'calc(100vw - var(--pb-osd-left, 0px) - (' + cfg.left + 'rem))';
        } else if (cfg.h === 'center') {
            img.style.left = '50%';
            img.style.right = 'auto';
            capW = '100vw';
        } else {
            img.style.left = 'auto';
            img.style.right = 'calc(var(--pb-osd-right, 0px) + ' + cfg.right + 'rem)';
            capW = 'calc(100vw - var(--pb-osd-right, 0px) - (' + cfg.right + 'rem))';
        }
        img.style.maxHeight = 'min(' + s.maxH + 'rem, ' + capH + ')';
        img.style.maxWidth = s.maxW > 0 ? 'min(' + s.maxW + 'rem, ' + capW + ')' : capW;
    }

    function paTransform(cfg, angle, shift) {
        return (cfg.h === 'center' ? 'translateX(-50%) ' : '') + (cfg.v === 'center' ? 'translateY(-50%) ' : '') + (shift ? 'translateY(' + shift + 'rem) ' : '')
            + (angle === null ? '' : 'rotate(' + angle + 'deg)');
    }

    function paImage(key, cfg) {
        var disc = key === 'disc';
        var img = document.createElement('img');
        img.className = 'pbPlayerArtLayer';
        img.setAttribute('data-pb-layer', key);
        img.alt = '';
        img.draggable = false;
        img.style.position = 'fixed';
        img.style.width = 'auto';
        img.style.pointerEvents = 'none';
        img.style.userSelect = 'none';
        if (disc) {
            img.style.transformOrigin = '50% 50%';
        } else {
            img.style.filter = 'drop-shadow(0 6px 12px rgba(0,0,0,0.55))';
        }
        img.style.opacity = '0';
        img.style.transition = 'opacity 0.25s ease';
        img.style.zIndex = String(cfg.z);
        img.style.display = 'none';
        img.style.transform = paTransform(cfg, disc ? 0 : null);
        paApply(img, cfg);
        img.addEventListener('transitionend', function (e) {
            if (e.propertyName === 'opacity' && img.style.opacity === '0') {
                img.style.display = 'none';
            }
        });
        paRoot.appendChild(img);
        return img;
    }

    function paWriteVars() {
        Object.keys(paVars).forEach(function (name) {
            var value = paVars[name].toFixed(2) + 'px';
            if (paRoot.style.getPropertyValue('--pb-osd-' + name) !== value) {
                paRoot.style.setProperty('--pb-osd-' + name, value);
            }
        });
    }

    // The anchors from the live OSD, against the probe's rounded viewport edge; a bar that cannot be measured (hidden) keeps the last values, marked
    // stale, and is measured again the moment it is shown.
    function paMeasure() {
        paTimer = null;
        var page = document.querySelector('#videoOsdPage:not(.hide)');
        if (!page || !paRoot) {
            return;
        }
        var probe = document.getElementById('pbPlayerArtProbe');
        if (!probe) {
            probe = document.createElement('div');
            probe.id = 'pbPlayerArtProbe';
            probe.style.cssText = 'position:fixed;right:0;bottom:0;width:0;height:0;pointer-events:none;visibility:hidden;';
            document.body.appendChild(probe);
        }
        var edge = probe.getBoundingClientRect();
        var ctrl = page.querySelector('.videoOsdBottom .osdControls');
        paStale = true;
        if (ctrl) {
            var r = ctrl.getBoundingClientRect();
            if (r.height > 0) {
                var cs = getComputedStyle(ctrl);
                paVars.bottom = Math.max(0, edge.top - r.top);
                paVars.left = Math.max(0, r.left + (parseFloat(cs.paddingLeft) || 0));
                paVars.right = Math.max(0, edge.left - r.right + (parseFloat(cs.paddingRight) || 0));
                paStale = false;
            }
        }
        var remPx = parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
        paVars.top = PA_HEADER_REM * remPx;
        var bottom = paVars.bottom === undefined ? 0 : parseFloat(paVars.bottom.toFixed(2));
        paVars.band = Math.max(0, edge.top - paVars.top - bottom);
        paWriteVars();
        var tight = paVars.band < PA_MIN_BAND_REM * remPx;
        if (tight !== paRoot.hasAttribute('data-pb-tight')) {
            if (tight) {
                paRoot.setAttribute('data-pb-tight', '');
            } else {
                paRoot.removeAttribute('data-pb-tight');
            }
        }
        if (ctrl !== paObservedCtrl) {
            if (paResize) {
                paResize.disconnect();
                if (ctrl) {
                    paResize.observe(ctrl);
                }
            }
            paObservedCtrl = ctrl;
        }
    }

    function paSync() {
        var page = document.querySelector('#videoOsdPage:not(.hide)');
        var bottom = page && page.querySelector('.videoOsdBottom');
        var ctrl = bottom && bottom.querySelector('.osdControls');
        var shown = !!ctrl && !bottom.classList.contains('hide');
        if (shown && (!paBarShown || paStale || ctrl !== paObservedCtrl)) {
            clearTimeout(paTimer);
            paMeasure();
        }
        paBarShown = shown;
    }

    function paSchedule() {
        if (!paTimer) {
            paTimer = setTimeout(paMeasure, 30);
        }
    }

    function paOnDpr() {
        paWatchDpr();
        paSchedule();
    }

    function paWatchDpr() {
        if (paDpr) {
            paDpr.removeEventListener('change', paOnDpr);
        }
        paDpr = window.matchMedia('(resolution: ' + window.devicePixelRatio + 'dppx)');
        paDpr.addEventListener('change', paOnDpr);
    }

    function paOsdVisible() {
        var header = [].some.call(document.querySelectorAll('.osdHeader'), function (h) {
            return getComputedStyle(h).opacity !== '0';
        });
        var bar = document.querySelector('#videoOsdPage:not(.hide) .videoOsdBottom');
        return header || !!bar && getComputedStyle(bar).opacity !== '0';
    }

    function paCurrentId() {
        var rating = document.querySelector('#videoOsdPage:not(.hide) .btnUserRating');
        return window.NowPlayingItem && window.NowPlayingItem.Id || rating && rating.getAttribute('data-id') || null;
    }

    function paReset() {
        paGen++;
        clearInterval(paCaTimer);
        paCaTimer = null;
        paExtra.forEach(function (e) {
            e.img.removeAttribute('src');
            e.img.removeAttribute('data-pb-show');
            e.img.style.opacity = '0';
            e.img.style.display = 'none';
        });
        Object.keys(paImgs).forEach(function (key) {
            var img = paImgs[key];
            img.removeAttribute('src');
            img.style.opacity = '0';
            img.style.display = 'none';
        });
        cancelAnimationFrame(paFrame);
        paFrame = null;
        paAngle = 0;
        paLast = null;
        paClearart = 'none';
    }

    function paRemove() {
        paReset();
        [paRoot, document.getElementById('pbPlayerArtProbe'), document.getElementById('pbPlayerArtStyle')].forEach(function (el) {
            if (el) {
                el.remove();
            }
        });
        paRoot = null;
        document.body.classList.remove('pbPlayerArtOn');
        paImgs = {};
        paExtra = [];
        paValues = null;
        if (paResize) {
            paResize.disconnect();
        }
        paObservedCtrl = null;
        paStale = true;
        paBarShown = false;
    }

    function paLoad(img, id, type, index, tag, done) {
        paLoadUrl(img, window.ApiClient.getUrl('Items/' + id + '/Images/' + type + (index === undefined ? '' : '/' + index), { tag: tag, maxHeight: 400 }), done);
    }

    function paLoadUrl(img, url, done) {
        var gen = paGen;
        var test = new Image();
        test.onload = function () {
            if (gen !== paGen || !img.isConnected) {
                return;
            }
            img.src = url;
            img.style.display = 'block';
            if (done) {
                done();
            }
            paUpdate();
        };
        test.onerror = function () {
            if (gen === paGen && img.isConnected) {
                img.removeAttribute('src');
                img.style.opacity = '0';
            }
        };
        test.src = url;
    }

    function paRotate(ts) {
        var disc = paImgs.disc;
        if (!disc || !paValues) {
            paFrame = null;
            return;
        }
        var video = document.querySelector('video');
        if (video && !video.paused && paLast === null) {
            paLast = ts;
        }
        if (disc.getAttribute('src') && video && !video.paused && !video.ended) {
            paAngle = (paAngle + 360 / (paValues.seconds * 1000) * (ts - paLast)) % 360;
            disc.style.transform = paTransform(paValues.layers.disc, paAngle);
            paExtra.forEach(function (e) {
                if (e.key === 'disc' && e.img.getAttribute('src')) {
                    e.img.style.transform = paTransform(paValues.layers.disc, paAngle, e.shift);
                }
            });
            paLast = ts;
        }
        if (video && video.paused) {
            paLast = null;
        }
        paFrame = requestAnimationFrame(paRotate);
    }

    // Resolves the artwork for the game profile: the chain is [extra, game]; Default takes both in that order, Extra the extra, Game the game.
    function paDraw(gen, values, chain) {
        if (gen !== paGen) {
            return;
        }
        if (osdDraws()) {
            paRemove();
            paItem = null;
            return;
        }
        if (!document.getElementById('pbPlayerArtStyle')) {
            var css = document.createElement('style');
            css.id = 'pbPlayerArtStyle';
            css.textContent = PA_CSS;
            document.head.appendChild(css);
        }
        if (!paRoot) {
            paRoot = document.createElement('div');
            paRoot.id = 'pbPlayerArt';
            document.body.appendChild(paRoot);
            document.body.classList.add('pbPlayerArtOn');
            paWriteVars();
        }
        paRoot.textContent = '';
        clearInterval(paCaTimer);
        paCaTimer = null;
        paImgs = {};
        paExtra = [];
        paDiscItem = null;
        paValues = values;
        var layers = values.layers;
        PA_LAYERS.forEach(function (l) {
            if (layers[l[0]].on) {
                paImgs[l[0]] = paImage(l[0], layers[l[0]]);
            }
        });
        var pick = function (cfg, has) {
            var items = cfg.source === 'Extra' ? chain.slice(0, 1) : cfg.source === 'Game' ? chain.slice(1, 2) : chain;
            return items.filter(function (item) {
                return item && has(item);
            })[0] || null;
        };
        var tagged = function (type) {
            return function (item) {
                return item.ImageTags && item.ImageTags[type];
            };
        };
        if (paImgs.clearart) {
            var art = pick(layers.clearart, tagged('Art'));
            var logo = !art && values.fallback ? pick(layers.clearart, tagged('Logo')) : null;
            if (art) {
                paLoad(paImgs.clearart, art.Id, 'Art', undefined, art.ImageTags.Art);
                paClearart = 'real';
            } else if (logo) {
                paLoad(paImgs.clearart, logo.Id, 'Logo', undefined, logo.ImageTags.Logo);
                paClearart = 'fallback';
            }
        }
        PA_LAYERS.forEach(function (l) {
            var img = paImgs[l[0]];
            if (!img || l[4] || l[0] === 'clearart' || l[0] === 'logo' && paHideLogo()) {
                return;
            }
            var index = l[3];
            var item = pick(layers[l[0]], index === undefined ? tagged(l[2]) : function (it) {
                return it.BackdropImageTags && it.BackdropImageTags.length > index;
            });
            if (!item) {
                return;
            }
            if (l[0] === 'disc') {
                paDiscItem = item.Id;
            }

            paLoad(img, item.Id, l[2], index, index === undefined ? item.ImageTags[l[2]] : item.BackdropImageTags[index], l[0] !== 'disc' ? null : function () {
                if (values.spin && !paFrame) {
                    paFrame = requestAnimationFrame(paRotate);
                }
            });
        });
        paDrawOwn(values, chain);
        paSchedule();
    }

    function paGetJson(path) {
        var api = window.ApiClient;
        return Promise.resolve().then(function () {
            return api.getJSON(api.getUrl(path));
        }).then(function (answer) {
            return answer instanceof Array ? answer : null;
        }, function () {
            return null;
        });
    }

    // The layers that come from the plugin's own routes (the game of the chain, never the extra): the single pictures, the further discs and the characterart rotation.
    function paDrawOwn(values, chain) {
        var game = chain[1];
        var api = window.ApiClient;
        var layers = values.layers;
        var gen = paGen;
        if (!game || !game.Id || !api) {
            return;
        }
        var live = function (img) {
            return gen === paGen && img && img.isConnected;
        };
        var own = function (key) {
            return paImgs[key] && layers[key].source !== 'Extra' ? paImgs[key] : null;
        };
        var singles = PA_LAYERS.filter(function (l) {
            return l[4] && l[4] !== 'characterart' && own(l[0]);
        });
        if (singles.length) {
            paGetJson('PlayniteBridge/games/' + game.Id + '/pictures').then(function (list) {
                (list || []).forEach(function (entry) {
                    singles.filter(function (l) {
                        return l[4] === entry.Type;
                    }).forEach(function (l) {
                        if (live(paImgs[l[0]])) {
                            paLoadUrl(paImgs[l[0]], api.getUrl('PlayniteBridge/games/' + game.Id + '/pictures/' + entry.Type, { tag: entry.Tag }));
                        }
                    });
                });
            });
        }
        // further discs: the first stays Jellyfin's own disc image in front, the others lie behind it, each a fixed step further from the edge the disc sits at
        // only behind the game's own first disc: with another item's disc in front (the video's), the game's discs would not follow it
        if (own('disc') && values.discMulti && values.discMax > 1 && paDiscItem === game.Id) {
            paGetJson('PlayniteBridge/games/' + game.Id + '/discs').then(function (list) {
                if (!list || !live(paImgs.disc)) {
                    return;
                }
                var behind = paImgs.disc;
                var step = layers.disc.v === 'top' ? 1.5 : -1.5;
                list.slice(1, values.discMax).forEach(function (entry, i) {
                    var img = paImage('disc', layers.disc);
                    paRoot.insertBefore(img, behind);
                    behind = img;
                    paExtra.push({ img: img, key: 'disc', shift: step * (i + 1) });
                    paLoadUrl(img, api.getUrl('PlayniteBridge/games/' + game.Id + '/discs/' + entry.Index, { tag: entry.Tag }), function () {
                        if (values.spin && !paFrame) {
                            paFrame = requestAnimationFrame(paRotate);
                        }
                    });
                });
            });
        }
        if (own('characterart')) {
            paGetJson('PlayniteBridge/games/' + game.Id + '/characterart?player=1').then(function (list) {
                var first = paImgs.characterart;
                if (!list || !list.length || !live(first)) {
                    return;
                }
                var url = function (entry) {
                    return api.getUrl('PlayniteBridge/games/' + game.Id + '/characterart/' + entry.Index, { tag: entry.Tag, player: 1 });
                };
                if (!values.caMulti || list.length < 2) {
                    paLoadUrl(first, url(list[0]));
                    return;
                }
                var second = paImage('characterart', layers.characterart);
                paExtra.push({ img: second, key: 'characterart', shift: 0 });
                var fade = Math.min(values.caFade, values.caCycle);
                [first, second].forEach(function (img) {
                    img.style.transition = 'opacity ' + fade + 'ms ease';
                });
                var order = [];
                var pos = 0;
                var shown = null;
                var last = -1;
                var startBy = values.caRandomStart ? Math.floor(Math.random() * list.length) : 0;
                var round = function () {
                    order = list.map(function (entry, i) {
                        return i;
                    });
                    if (values.caShuffle) {
                        for (var i = order.length - 1; i > 0; i--) {
                            var j = Math.floor(Math.random() * (i + 1));
                            var swap = order[i];
                            order[i] = order[j];
                            order[j] = swap;
                        }
                        if (order[0] === last && order.length > 1) {
                            order.push(order.shift());
                        }
                    } else {
                        order = order.slice(startBy).concat(order.slice(0, startBy));
                    }
                    pos = 0;
                };
                var show = function () {
                    var hidden = shown === first ? second : first;
                    last = order[pos];
                    paLoadUrl(hidden, url(list[order[pos]]), function () {
                        // the incoming image has been shown (display) but not yet laid out at opacity 0: without this the change of opacity would not run as a transition
                        void hidden.offsetWidth;
                        if (shown) {
                            shown.removeAttribute('data-pb-show');
                        }
                        hidden.setAttribute('data-pb-show', '1');
                        shown = hidden;
                    });
                };
                round();
                show();
                paCaTimer = setInterval(function () {
                    if (!live(first)) {
                        clearInterval(paCaTimer);
                        paCaTimer = null;
                        return;
                    }
                    if (++pos >= order.length) {
                        round();
                    }
                    show();
                }, values.caCycle);
            });
        }
    }

    // The game of a playing extra and the two items; not an extra of a game, no PlayerArtwork or an error: nothing of ours. An item this tab did not
    // see in a game's lists (extraIds) is asked once from the server (PlayniteBridge/extras/{id}/game) and kept, a null answer as false.
    function paResolve(id) {
        var api = window.ApiClient;
        if (!api) {
            paRemove();
            return;
        }
        var gen = paGen;
        var game = id in extraIds ? Promise.resolve(extraIds[id] || null) : artConfiguration(api).then(function (settings) {
            if (!settings || !settings.PlayerArtwork) {
                return null;
            }
            return api.getJSON(api.getUrl('PlayniteBridge/extras/' + encodeURIComponent(id) + '/game')).then(function (answer) {
                if (!extraIds[id]) {
                    extraIds[id] = answer && answer.id || false;
                }
                return extraIds[id] || null;
            });
        });
        Promise.all([artConfiguration(api), game]).then(function (results) {
            var config = results[0];
            var gameId = results[1];
            if (gen !== paGen) {
                return;
            }
            if (!gameId || !config || !config.PlayerArtwork) {
                paRemove();
                return;
            }
            var user = api.getCurrentUserId();
            return Promise.all([api.getItem(user, id), api.getItem(user, gameId)]).then(function (items) {
                paDraw(gen, paRead(config.PlayerArtwork), items);
            });
        }).catch(function () {
            if (gen === paGen) {
                paRemove();
            }
        });
    }

    function paUpdate() {
        if (!paOn) {
            return;
        }
        if (osdDraws()) {
            if (paRoot || paItem) {
                paRemove();
                paItem = null;
            }
            return;
        }
        var id = window.location.href.indexOf('#/video') === -1 ? null : paCurrentId();
        if (!id) {
            paReset();
            paItem = null;
            return;
        }
        if (id !== paItem) {
            paItem = id;
            paReset();
            paResolve(id);
        }
        if (!paRoot || !paValues) {
            return;
        }
        paSync();
        var visible = paOsdVisible();
        var layers = paValues.layers;
        PA_LAYERS.forEach(function (l) {
            var img = paImgs[l[0]];
            var cfg = layers[l[0]];
            if (!img || !img.getAttribute('src')) {
                return;
            }
            if (l[0] === 'logo' && paHideLogo()) {
                img.style.display = 'none';
                img.style.opacity = '0';
                return;
            }
            img.style.zIndex = String(cfg.z);
            paApply(img, cfg, l[0] === 'clearart' && paClearart === 'fallback' ? layers.logo : null);
            img.style.transform = paTransform(cfg, l[0] === 'disc' ? paAngle : null);
            img.style.display = 'block';
            img.style.opacity = visible && (l[0] !== 'characterart' || !paExtra.some(function (e) {
                return e.key === 'characterart';
            }) || img.hasAttribute('data-pb-show')) ? '1' : '0';
        });
        paExtra.forEach(function (e) {
            if (!e.img.getAttribute('src')) {
                return;
            }
            var cfg = layers[e.key];
            e.img.style.zIndex = String(cfg.z);
            paApply(e.img, cfg);
            e.img.style.transform = paTransform(cfg, e.key === 'disc' ? paAngle : null, e.shift);
            e.img.style.display = 'block';
            e.img.style.opacity = visible && (e.key !== 'characterart' || e.img.hasAttribute('data-pb-show')) ? '1' : '0';
        });
    }

    function paMutations(records) {
        if (records.some(function (r) {
            return !paRoot || !paRoot.contains(r.target);
        })) {
            paUpdate();
        }
    }

    function paStop() {
        paSession++;
        if (paOn) {
            paOn = false;
            paObserver.disconnect();
            paObserver = null;
            window.removeEventListener('resize', paSchedule);
            document.removeEventListener('fullscreenchange', paSchedule);
            document.removeEventListener('webkitfullscreenchange', paSchedule);
            paDpr.removeEventListener('change', paOnDpr);
            paDpr = null;
        }
        clearTimeout(paTimer);
        paTimer = null;
        paRemove();
        paResize = null;
        paItem = null;
    }

    function paStart() {
        paOn = true;
        paResize = typeof ResizeObserver === 'function' ? new ResizeObserver(paSchedule) : null;
        window.addEventListener('resize', paSchedule);
        document.addEventListener('fullscreenchange', paSchedule);
        document.addEventListener('webkitfullscreenchange', paSchedule);
        paWatchDpr();
        paObserver = new MutationObserver(paMutations);
        paObserver.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['style', 'class'] });
        paUpdate();
    }

    document.addEventListener('viewshow', function (e) {
        paStop();
        var api = window.ApiClient;
        if (!e.target || e.target.id !== 'videoOsdPage' || !api) {
            return;
        }
        var session = paSession;
        artConfiguration(api).then(function (config) {
            if (session === paSession && config && config.PlayerArtwork) {
                paStart();
            }
        }, function () {
        });
    });
    window.addEventListener('hashchange', function () {
        if (window.location.href.indexOf('#/video') === -1) {
            paStop();
        } else if (paOn) {
            paReset();
            paItem = null;
        }
    });


    // Themes tab: theme music and theme videos, the plugin's own players, independent of Jellyfin's theme song and theme video settings (the server
    // leaves what it plays out of Jellyfin's own answers, so nothing plays twice). Any other media that starts playing stops both themes, and none
    // starts while other media plays; a theme video with sound keeps the music from playing, a muted one plays beside it. The video lies behind the
    // page in place of the backdrop images.
    // Both start on the viewshow of a game page and stop when it is left; the synthetic viewshow of a version switch follows On Version Switch (Keep
    // Playing, or Look for New Theme, which keeps the playing song if the new answer has it). Loop, delays and fades are decided by themePlan.
    var THEME_VIDEO_CSS = 'body.pbThemeVideoOn .backdropContainer,body.pbThemeVideoOn .pbBackdrop,body.pbThemeVideoOn .artworkplus-own-backdrop{visibility:hidden !important}'
        + '.pbThemeVideo{position:fixed;inset:0;z-index:-1;width:100%;height:100%;object-fit:cover;opacity:0;transition:opacity .5s ease;background:#000;pointer-events:none}'
        + '.pbThemeVideo.pbThemeVideoShown{opacity:1}';

    function themeShuffle(list, avoid) {
        var copy = list.slice();
        for (var i = copy.length - 1; i > 0; i--) {
            var j = Math.floor(Math.random() * (i + 1));
            var t = copy[i];
            copy[i] = copy[j];
            copy[j] = t;
        }
        if (copy.length > 1 && avoid && copy[0].Id === avoid) {
            copy.push(copy.shift());
        }
        return copy;
    }

    function themeShuffleGrouped(list, avoid) {
        var order = [];
        var groups = {};
        list.forEach(function (item) {
            if (!groups[item.Source]) {
                groups[item.Source] = [];
                order.push(item.Source);
            }
            groups[item.Source].push(item);
        });
        return order.reduce(function (all, source, i) {
            return all.concat(themeShuffle(groups[source], i === 0 ? avoid : null));
        }, []);
    }

    // The scheduling decisions of the theme players, pure.
    // s: Loop, DelayedStart, DelayedStartEvery, FadeIn, FadeOut, FadeEvery, n: items in the queue, index: the item that plays. Events: 'start',
    // 'next', 'repeat'. The fade out comes before every end with FadeEvery, else only before the very end. A single item with Loop and nothing to do
    // at its repeat plays with the element's own loop, without a gap.
    function themePlan(s, n, index, event) {
        var delay = Math.max(0, s.DelayedStart || 0);
        var fadeIn = Math.max(0, s.FadeIn || 0);
        var fadeOut = Math.max(0, s.FadeOut || 0);
        var first = event === 'start';
        var native = n === 1 && !!s.Loop && !s.StartRandom && !(s.DelayedStartEvery && delay > 0) && (!s.FadeEvery || (fadeIn === 0 && fadeOut === 0));
        return {
            delay: first || s.DelayedStartEvery ? delay : 0,
            fadeIn: first || s.FadeEvery ? fadeIn : 0,
            fadeOut: !native && (s.FadeEvery || (index === n - 1 && !s.Loop)) ? fadeOut : 0,
            native: native
        };
    }

    function themeAfterEnd(s, n, index) {
        if (index + 1 < n) {
            return { action: 'next', index: index + 1 };
        }
        return s.Loop ? { action: 'repeat', index: 0 } : { action: 'stop', index: index };
    }

    // One player: music (an Audio element, fades) or video (a <video> behind the page, a short opacity fade).
    function createThemePlayer(kind) {
        var video = kind === 'video';
        var p = {
            kind: kind,
            token: 0,
            el: null,
            queue: [],
            index: 0,
            name: null,
            settings: null,
            delayTimer: null,
            fadeTimer: null,
            fadingOut: false,
            gesture: false
        };

        p.active = function () {
            return !!p.el && !!p.name;
        };

        p.fade = function (to, seconds, done) {
            clearInterval(p.fadeTimer);
            var el = p.el;
            if (!el) {
                return;
            }
            if (!(seconds > 0)) {
                el.volume = to;
                if (done) {
                    done();
                }
                return;
            }
            var from = el.volume;
            var steps = Math.max(2, Math.round(seconds * 10));
            var n = 0;
            p.fadeTimer = setInterval(function () {
                n++;
                el.volume = Math.max(0, Math.min(1, from + (to - from) * n / steps));
                if (n >= steps) {
                    clearInterval(p.fadeTimer);
                    if (done) {
                        done();
                    }
                }
            }, seconds * 1000 / steps);
        };

        function release(el) {
            if (!el) {
                return;
            }
            el.onended = null;
            el.ontimeupdate = null;
            el.pause();
            el.removeAttribute('src');
            el.load();
            if (video && el.parentNode) {
                el.parentNode.removeChild(el);
            }
        }

        p.stop = function (fadeSeconds) {
            p.token++;
            clearTimeout(p.delayTimer);
            p.delayTimer = null;
            clearInterval(p.fadeTimer);
            var el = p.el;
            p.el = null;
            p.name = null;
            p.queue = [];
            p.fadingOut = false;
            if (!el) {
                return;
            }
            el.onended = null;
            el.ontimeupdate = null;
            if (video) {
                el.classList.remove('pbThemeVideoShown');
                document.body.classList.remove('pbThemeVideoOn');
                setTimeout(function () { release(el); }, 500);
                return;
            }
            var seconds = fadeSeconds === undefined ? (p.settings && p.settings.FadeOut) || 0 : fadeSeconds;
            if (!(seconds > 0)) {
                release(el);
                return;
            }
            var from = el.volume;
            var steps = Math.max(2, Math.round(seconds * 10));
            var n = 0;
            var timer = setInterval(function () {
                n++;
                el.volume = Math.max(0, from * (1 - n / steps));
                if (n >= steps) {
                    clearInterval(timer);
                    release(el);
                }
            }, seconds * 1000 / steps);
        };

        function level() {
            return Math.max(0, Math.min(1, ((p.settings && p.settings.Volume) === undefined ? 40 : p.settings.Volume) / 100));
        }

        function element() {
            if (p.el) {
                return p.el;
            }
            if (video) {
                p.el = document.createElement('video');
                p.el.className = 'pbThemeVideo';
                p.el.playsInline = true;
                p.el.setAttribute('playsinline', '');
                document.body.insertBefore(p.el, document.body.firstChild);
            } else {
                p.el = new Audio();
            }
            return p.el;
        }

        function ensureStyle() {
            if (!document.getElementById('pbThemeVideoStyle')) {
                var css = document.createElement('style');
                css.id = 'pbThemeVideoStyle';
                css.textContent = THEME_VIDEO_CSS;
                document.head.appendChild(css);
            }
        }

        // The item at p.index for the event ('start', 'next', 'repeat'): after the delay of themePlan, then playCurrent. The name counts as playing
        // during the delay (a version switch).
        p.schedule = function (api, token, event) {
            var song = p.queue[p.index];
            if (!song || token !== p.token) {
                return;
            }
            var plan = themePlan(p.settings, p.queue.length, p.index, event);
            if (plan.delay > 0) {
                p.name = song.Name;
                p.delayTimer = setTimeout(function () {
                    p.delayTimer = null;
                    p.playCurrent(api, token, plan);
                }, plan.delay * 1000);
            } else {
                p.playCurrent(api, token, plan);
            }
        };

        p.playCurrent = function (api, token, plan) {
            var song = p.queue[p.index];
            if (!song || token !== p.token) {
                return;
            }
            var el = element();
            el.loop = plan.native;
            if (video) {
                ensureStyle();
                el.muted = level() === 0;
            }
            p.fadingOut = false;
            el.onended = function () {
                if (token !== p.token) {
                    return;
                }
                var after = themeAfterEnd(p.settings, p.queue.length, p.index);
                if (after.action === 'stop') {
                    p.stop(0);
                    return;
                }
                p.index = after.index;
                if (after.action === 'repeat' && p.settings.PlayMode === 'AllShuffled') {
                    p.queue = video && p.settings.KeepSourcesTogether ? themeShuffleGrouped(p.queue, song.Id) : themeShuffle(p.queue, song.Id);
                }
                p.schedule(api, token, after.action);
            };
            p.name = song.Name;
            var wanted = level();
            el.volume = video ? wanted : 0;
            el.src = tokenUrl(api, (video ? 'Videos/' : 'Audio/') + song.Id + '/stream', { static: true });
            if (!video) {
                var fadeOut = plan.fadeOut;
                el.ontimeupdate = function () {
                    if (fadeOut > 0 && !p.fadingOut && isFinite(el.duration) && el.duration - el.currentTime <= fadeOut) {
                        p.fadingOut = true;
                        p.fade(0, Math.max(0.2, el.duration - el.currentTime));
                    }
                };
                p.fadeInSeconds = plan.fadeIn;
            }
            if (p.settings.StartRandom) {
                var seek = function () {
                    el.removeEventListener('loadedmetadata', seek);
                    if (token === p.token && isFinite(el.duration) && el.duration > 0) {
                        var low = Math.max(0, p.settings.StartMin || 0);
                        var high = Math.max(low, p.settings.StartMax === undefined ? 50 : p.settings.StartMax);
                        el.currentTime = el.duration * (low + Math.random() * (high - low)) / 100;
                    }
                };
                el.addEventListener('loadedmetadata', seek);
            }
            var started = el.play();
            var shown = function () {
                if (token !== p.token) {
                    return;
                }
                if (video) {
                    document.body.classList.add('pbThemeVideoOn');
                    el.classList.add('pbThemeVideoShown');
                } else {
                    p.fade(wanted, p.fadeInSeconds);
                }
            };
            if (started && started.then) {
                started.then(shown, function () {
                    if (!p.gesture) {
                        p.gesture = true;
                        var retry = function () {
                            document.removeEventListener('click', retry, true);
                            document.removeEventListener('keydown', retry, true);
                            p.gesture = false;
                            if (p.el === el && token === p.token) {
                                el.play().then(shown, function () { });
                            }
                        };
                        document.addEventListener('click', retry, true);
                        document.addEventListener('keydown', retry, true);
                    }
                });
            }
        };

        p.begin = function (api, songs) {
            var settings = p.settings;
            var list = songs.slice();
            if (settings.PlayMode === 'FirstOnly') {
                list = list.slice(0, 1);
            } else if (settings.PlayMode === 'RandomOne') {
                list = [list[Math.floor(Math.random() * list.length)]];
            } else if (settings.PlayMode === 'AllShuffled') {
                list = video && settings.KeepSourcesTogether ? themeShuffleGrouped(list, null) : themeShuffle(list, null);
            }
            var token = ++p.token;
            clearTimeout(p.delayTimer);
            p.delayTimer = null;
            if (p.el) {
                p.el.onended = null;
                p.el.ontimeupdate = null;
                p.el.pause();
            }
            p.queue = list;
            p.index = 0;
            p.schedule(api, token, 'start');
        };

        // The game page of id: asks the songs or videos and starts, continues or stops them; resolves with whether this player plays with sound
        // above 0 after it (the video player's answer keeps the music from playing).
        p.show = function (api, id, inPlace, config, blocked) {
            var settings = config && config[video ? 'ThemeVideos' : 'ThemeMusic'];
            p.settings = settings || p.settings;
            if (!settings) {
                p.stop();
                return Promise.resolve(false);
            }
            var playing = p.active() || p.delayTimer ? p.name : null;
            if (inPlace && playing && settings.OnVersionSwitch === 'KeepPlaying') {
                return Promise.resolve(video && settings.Volume > 0);
            }
            if (blocked && blocked()) {
                p.stop(0);
                return Promise.resolve(false);
            }
            var token = ++p.token;
            var query = (video ? { kind: 'video' } : {});
            if (playing && inPlace) {
                query.playing = playing;
            }
            return api.ajax({ type: 'GET', url: api.getUrl('PlayniteBridge/games/' + encodeURIComponent(id) + '/themes', query) }).then(function (response) {
                if (token !== p.token) {
                    return null;
                }
                return response && response.status === 200 ? response.json() : [];
            }).then(function (songs) {
                if (token !== p.token || !songs) {
                    return video && settings.Volume > 0 && p.active();
                }
                if (!songs.length) {
                    p.stop();
                    return false;
                }
                if (playing && inPlace && (p.active() || p.delayTimer) && songs.some(function (song) { return song.Name.toLowerCase() === playing.toLowerCase(); })) {
                    return video && settings.Volume > 0;
                }
                p.begin(api, songs);
                return video && settings.Volume > 0;
            }, function () { return false; });
        };

        return p;
    }

    var themeMusic = createThemePlayer('music');
    var themeVideo = createThemePlayer('video');

    function themeOtherMediaPlaying() {
        return [].some.call(document.querySelectorAll('audio,video'), function (m) {
            return m !== themeMusic.el && m !== themeVideo.el && !m.paused && !m.ended && m.currentTime > 0;
        });
    }

    function themeStopAll() {
        themeMusic.stop();
        themeVideo.stop();
    }

    document.addEventListener('play', function (e) {
        var target = e.target;
        if (target && target !== themeMusic.el && target !== themeVideo.el && (target instanceof HTMLMediaElement) && (themeMusic.active() || themeVideo.active())) {
            themeStopAll();
        }
    }, true);

    function themeShow(id, inPlace) {
        var api = window.ApiClient;
        if (!api || !id) {
            return;
        }
        Promise.all([clientConfiguration(api), loadGame(api, id)]).then(function (results) {
            var config = results[0];
            if (!results[1]) {
                themeStopAll();
                return;
            }
            var noSound = false;
            return themeVideo.show(api, id, inPlace, config, themeOtherMediaPlaying).then(function (sound) {
                noSound = sound;
                return themeMusic.show(api, id, inPlace, config, function () { return noSound || themeOtherMediaPlaying(); });
            });
        }).catch(function () { });
    }

    document.addEventListener('viewshow', function (e) {
        var view = e.target;
        if (!view || view.id !== 'itemDetailPage') {
            return;
        }
        var params = e.detail && e.detail.params;
        var id = params && typeof params.id === 'string' ? params.id : itemId();
        themeShow(id, !!(e.detail && e.detail.pbInPlace));
    });

    document.addEventListener('viewbeforehide', function (e) {
        if (e.target && e.target.id === 'itemDetailPage' && !(e.detail && e.detail.pbInPlace) && (themeMusic.active() || themeVideo.active() || themeMusic.delayTimer || themeVideo.delayTimer)) {
            themeStopAll();
        }
    });

    // Late start on a cold load of a detail URL: this deferred script comes after Jellyfin's bundles, which can show the page before the listeners
    // above exist. When a detail page is shown at load and no viewshow follows within a second, it is shown again as a version switch does it
    // (viewbeforehide + viewshow), so Jellyfin's controller reloads the item through the wrapped getItem.
    var lateView = document.querySelector('#itemDetailPage:not(.hide)');
    if (lateView && /^#\/details\?/.test(window.location.hash)) {
        var lateSeen = false;
        var onLateShow = function (e) {
            if (e.target === lateView) {
                lateSeen = true;
            }
        };
        document.addEventListener('viewshow', onLateShow, true);
        // Only a game's page is shown again: the item is asked through the wrapped getItem (Jellyfin's own request) and must be a possible game
        // (Type Movie, LocationType Virtual), and games/{id} must say so (loadGame); every other page stays as Jellyfin drew it.
        setTimeout(function () {
            document.removeEventListener('viewshow', onLateShow, true);
            var lateHash = window.location.hash;
            var lateItem = itemId();
            var lateApi = window.ApiClient;
            var stillLateView = function () {
                return !lateSeen && lateView.isConnected && !lateView.classList.contains('hide') && window.location.hash === lateHash;
            };
            if (!stillLateView() || !/^#\/details\?/.test(lateHash) || !lateItem || !lateApi) {
                return;
            }
            lateApi.getItem(lateApi.getCurrentUserId(), lateItem).then(function (item) {
                return item && item.Type === 'Movie' && item.LocationType === 'Virtual' ? loadGame(lateApi, lateItem) : null;
            }).then(function (game) {
                if (game && stillLateView()) {
                    var detail = { type: lateView.getAttribute('data-type'), params: {}, isRestored: false, options: {} };
                    lateView.dispatchEvent(new CustomEvent('viewbeforehide', { bubbles: true, detail: detail }));
                    lateView.dispatchEvent(new CustomEvent('viewshow', { bubbles: true, detail: detail }));
                }
            }).catch(noop);
        }, 1000);
    }

    // The same late start on a cold load of a games library page (movies.html or list.html of the library or a collection folder), which missed our
    // viewshow handlers. When it is shown at load, no viewshow follows within a second and the ClientConfiguration answer says it is the games
    // library, it is hidden and shown again once as a return from Jellyfin's view cache does it (isRestored true), which keeps Jellyfin's controllers
    // balanced (a non-restored replay would leave a second AlphaPicker and keydown listener).
    var lateLibrary = visibleView();
    var lateLibraryId = routeParam('movies.html', 'topParentId') || routeParam('list.html', 'parentId');
    var lateLibraryHash = window.location.hash;
    if (lateLibrary && lateLibraryId && lateLibrary !== reactLibraryPage()) {
        var lateLibrarySeen = false;
        var onLateLibraryShow = function (e) {
            if (e.target === lateLibrary) {
                lateLibrarySeen = true;
            }
        };
        document.addEventListener('viewshow', onLateLibraryShow, true);
        setTimeout(function () {
            var stillLate = function () {
                return !lateLibrarySeen && lateLibrary.isConnected && visibleView() === lateLibrary && window.location.hash === lateLibraryHash;
            };
            if (!stillLate() || !window.ApiClient) {
                document.removeEventListener('viewshow', onLateLibraryShow, true);
                return;
            }
            clientConfiguration(window.ApiClient).then(function (config) {
                document.removeEventListener('viewshow', onLateLibraryShow, true);
                if (stillLate() && gamesRoute(config, lateLibraryId.replace(/-/g, '').toLowerCase())) {
                    var detail = { type: lateLibrary.getAttribute('data-type'), params: {}, isRestored: true, options: {} };
                    lateLibrary.dispatchEvent(new CustomEvent('viewbeforehide', { bubbles: true, detail: detail }));
                    lateLibrary.dispatchEvent(new CustomEvent('viewhide', { bubbles: true, detail: detail }));
                    lateLibrary.dispatchEvent(new CustomEvent('viewshow', { bubbles: true, detail: detail }));
                }
            }, function () {
                document.removeEventListener('viewshow', onLateLibraryShow, true);
            });
        }, 1000);
    }

    // The Games library icon from the cached hint and choice, before the first answer.
    applyLibraryIcon();

})();
