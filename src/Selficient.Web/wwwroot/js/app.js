// @ts-check
/**
 * Selficient entry point. One module per page concern, each registered here. Behaviour attaches by
 * element or data attribute, never by route, and everything degrades: the server renders a complete
 * page before this file runs.
 */
import { DaysReady } from "selficient/elements/days-ready.js";
import { initTheme } from "selficient/theme.js";
import { initDialogs } from "selficient/dialogs.js";
import { initPlanPage } from "selficient/plan-page.js";
import { initHouseMap } from "selficient/house-map.js";
import { initShell } from "selficient/shell.js";
import { initInstall } from "selficient/install.js";
import { initPhotos } from "selficient/photos.js";
import { initBoard, initVerdictBar } from "selficient/board.js";
import { initLocate } from "selficient/locate.js";
import { initShare } from "selficient/share.js";
import { initKeep } from "selficient/keep.js";
import { initSendCopy } from "selficient/handoff.js";
import { initReceive } from "selficient/receive.js";
import { initAccount } from "selficient/account.js";
import { initSync } from "selficient/sync.js";
import { initPasskeys } from "selficient/passkeys.js";
import { initShortlist } from "selficient/shortlist.js";
import { initWizard } from "selficient/wizard.js";
import { initFigures } from "selficient/figures.js";
import { initStreet } from "selficient/street.js";

if (!customElements.get("days-ready")) {
  customElements.define("days-ready", DaysReady);
}

initShell(document);
initInstall(document);
initTheme(document);
initDialogs(document);
void initPlanPage(document);
void initWizard(document);
void initFigures(document);
void initStreet(document);
initBoard(document);
initVerdictBar(document);
initLocate(document);
initShare(document);
void initKeep(document);
initSendCopy(document);
void initReceive(document);
initAccount(document);
initSync(document);
void initPasskeys(document);
void initShortlist(document);
void initHouseMap(document);
initPhotos(document);
