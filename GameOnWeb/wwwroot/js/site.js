function onPageLoad()
{
    fetch('game/start')
        .then(response => response.text())
        .then(text => displayContent(text));
}

function onReactionClick(
    reactionText)
{
    fetch('game/reaction?reactionText=' + reactionText)
        .then(response => response.text())
        .then(text => displayContent(text));
    return false; // Ignore the href.
}

function onUndo()
{
    fetch('game/undo')
        .then(response => response.text())
        .then(text => displayContent(text));
    return false; // Ignore the href.
}

function displayContent(
    content)
{
    document.getElementById('content').innerHTML = content;
}

/* Didn't need this after all.

function getCookie(
    nameToGet)
{
    var decodedCookie = decodeURIComponent(document.cookie);
    var pieces = decodedCookie.split(';');
    for (var i = 0; i < pieces.length; ++i)
    {
        var nameValue = pieces[i].split('=');
        if (nameToGet == nameValue[0])
            return nameValue[1];
    }
    return "";
}
*/