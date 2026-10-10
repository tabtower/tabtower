namespace TabTower.Cli;

/// <summary>What <c>session end --close-tab</c> answers, in one place so it can be tested
/// without the app (tests/phone/PhoneHarness links this file).
///
/// The tab half has three outcomes, and the third used to borrow the wording of another verb.
/// A session that had ALREADY ended when the command arrived got the refusal that
/// <c>session close-tab</c> gives, which ends with "pass --close-tab to session end instead":
/// advice to run the very command that had just been run. It also said the tab "was left
/// open", which the deck does not know: on the live run that found this (0.11.17) the tab had
/// already been closed. What is true there is narrower, and that is what the reply now says.</summary>
public static class SessionEndReply
{
    /// <param name="endMessage">What ending the session reported, e.g. <c>session X ended</c>.</param>
    /// <param name="endedBefore">The session was already closed when the command arrived.</param>
    /// <param name="tabAsked">The connector was asked to close the tab.</param>
    /// <param name="whyNot">Why it was not asked, when it was not.</param>
    public static string WithCloseTab(string sessionId, string endMessage, bool endedBefore, bool tabAsked, string whyNot)
    {
        if (endedBefore)
            return $"session {sessionId} had already ended before this command; its VSCode tab was not asked for, " +
                   "because revealing an ended session would resume it off its transcript. " +
                   "If that tab is still open, close it by hand";
        return endMessage + (tabAsked ? "; closing its VSCode tab" : $"; its VSCode tab was left open: {whyNot}");
    }
}
