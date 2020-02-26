function onPageLoad()
{
    userId = getCookie("userid");
    if (userId == "")
    {
        fetch('game/newuser')
            .then(response => response.text())
            .then(text => display(text));
    }
    else
    {
        gameId = getCookie("gameid");
        fetch('game/existinggame?userid=' + userId + '&gameid=' + gameId)
            .then(response => response.text())
            .then(text => display(text));
    }
}

function display(
    content)
{
    document.getElementById('content').innerHTML = content;
}

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