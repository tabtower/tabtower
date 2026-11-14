// node out/closeChoice.test.js (npm test): when a close by id may skip the reveal. No VS Code needed.
import { inPlaceTarget, labelMatches } from './closeChoice';

let failed = 0;
function check(ok: boolean, what: string): void {
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${what}`);
    if (!ok) {
        failed++;
    }
}

const tab = (label: string, claude = true) => ({ label, claude });
const mine = tab('Fix the login page');
const other = tab('Another session');
const file = tab('Fix the login page', false);   // an editor with the same text, not a Claude tab

check(inPlaceTarget([other, mine, file], ['Fix the login page'], true) === mine,
      'one Claude tab carries the label and the deck vouched: closed in place');
check(inPlaceTarget([other, mine], ['Fix the login page'], false) === undefined,
      'no vouching from the deck (or a deck before 0.11.51): reveal');
check(inPlaceTarget([mine, tab('Fix the login page')], ['Fix the login page'], true) === undefined,
      'two Claude tabs carry the label (a leftover or a fork): reveal');
check(inPlaceTarget([tab('Claude Code'), tab('Claude Code')], ['Claude Code'], true) === undefined,
      'several unprompted "Claude Code" tabs: reveal');
check(inPlaceTarget([other], ['Fix the login page'], true) === undefined,
      'no tab carries the label: reveal, which can still find the panel by id');
check(inPlaceTarget([mine], [], true) === undefined, 'no labels at all: reveal');
check(inPlaceTarget([tab('Fix the login…'), other], ['Fix the login page'], true)?.label === 'Fix the login…',
      'a truncated label still names the one tab');
check(labelMatches('Fix the login…', ['Fix the login page']) && !labelMatches('Fix the logout…', ['Fix the login page']),
      'labelMatches: truncation matches a prefix only');

console.log(failed === 0 ? 'all passed' : `${failed} failed`);
process.exit(failed === 0 ? 0 : 1);
