const functions = require('firebase-functions');
const admin     = require('firebase-admin');
const https     = require('https');

admin.initializeApp();

const SESSION_WINDOW_SECS  = 30 * 60;
const SEVEN_DAYS_SECS      = 7 * 24 * 60 * 60;
const NEARBY_RADIUS_KM     = 1.0;
const LOCATION_FRESH_SECS  = 2 * 60 * 60;
const NEARBY_COOLDOWN_SECS = 60 * 60;

// ── Re-engagement ("Fancy a walk?") ──────────────────────────────────────────

const REENGAGEMENT_COPY = [
  { title: 'Fancy a walk?',            body: 'There are notes out there waiting to be found.' },
  { title: 'Step outside.',            body: 'Someone left a note near you.' },
  { title: 'Time for an adventure?',   body: 'Pick a direction and see what you discover.' },
  { title: 'The map is calling.',      body: 'Notes are waiting to be read.' },
  { title: 'Haven\'t seen you lately.',body: 'Come find out what\'s been posted near you.' },
];

exports.weeklyReEngagement = functions
  .region('europe-west2')
  .pubsub.schedule('every saturday 10:00')
  .timeZone('Europe/London')
  .onRun(async () => {
    const db      = admin.firestore();
    const nowSecs = Math.floor(Date.now() / 1000);
    const cutoff  = nowSecs - SEVEN_DAYS_SECS;

    const usersSnap = await db.collection('UserProfiles')
      .where('lastSeen', '<=', cutoff)
      .get();

    if (usersSnap.empty) return null;

    const eligible = [];

    for (const doc of usersSnap.docs) {
      const u = doc.data();
      if (!u.fcmToken) continue;
      if ((u.lastReadNotifiedAt   || 0) > cutoff) continue;
      if ((u.lastNearbyNotifiedAt || 0) > cutoff) continue;
      if ((u.lastWalkReminderAt   || 0) > cutoff) continue;
      if (!u.lastSeen) continue;
      eligible.push({ userId: doc.id, token: u.fcmToken });
    }

    if (eligible.length === 0) return null;

    const copy  = REENGAGEMENT_COPY[Math.floor(Math.random() * REENGAGEMENT_COPY.length)];
    const CHUNK = 500;

    for (let i = 0; i < eligible.length; i += CHUNK) {
      const chunk = eligible.slice(i, i + CHUNK);

      const result = await admin.messaging().sendEachForMulticast({
        tokens: chunk.map(u => u.token),
        notification: { title: copy.title, body: copy.body },
        data: { type: 'reengagement' },
        apns:    { headers: { 'apns-collapse-id': 'reengagement' } },
        android: { collapseKey: 'reengagement' },
      });

      const batch = db.batch();
      result.responses.forEach((resp, idx) => {
        const { userId } = chunk[idx];
        const ref = db.collection('UserProfiles').doc(userId);
        if (resp.success) {
          batch.update(ref, { lastWalkReminderAt: nowSecs });
        } else if (isInvalidToken(resp.error?.code)) {
          batch.update(ref, { fcmToken: admin.firestore.FieldValue.delete() });
        }
      });
      await batch.commit();
    }

    return null;
  });

// ── Story read notification ───────────────────────────────────────────────────

exports.onStoryRead = functions
  .region('europe-west2')
  .firestore.document('StoryReads/{readId}')
  .onCreate(async (snap) => {
    const data = snap.data();
    if (!data) return null;

    const { storyId, authorId, readerId } = data;
    if (!storyId || !authorId || !readerId) return null;
    if (authorId === readerId) return null;

    const db = admin.firestore();

    const [authorSnap, storySnap] = await Promise.all([
      db.collection('UserProfiles').doc(authorId).get(),
      db.collection('Stories').doc(storyId).get(),
    ]);

    if (!authorSnap.exists) return null;

    const authorData = authorSnap.data();
    const fcmToken   = authorData.fcmToken;
    if (!fcmToken) return null;

    const nowSecs      = Math.floor(Date.now() / 1000);
    const lastNotified = authorData.lastReadNotifiedAt || 0;
    if ((nowSecs - lastNotified) < SESSION_WINDOW_SECS) return null;

    const storyTitle = storySnap.exists ? storySnap.data()?.Title : null;
    const authorRef  = db.collection('UserProfiles').doc(authorId);

    try {
      await admin.messaging().send({
        notification: {
          title: 'Someone read your note',
          body: storyTitle ? `"${storyTitle}"` : 'A reader just discovered your writing.',
        },
        data:    { type: 'story_read', storyId },
        apns:    { headers: { 'apns-collapse-id': authorId } },
        android: { collapseKey: authorId },
        token:   fcmToken,
      });
      await authorRef.update({ lastReadNotifiedAt: nowSecs });
    } catch (err) {
      if (isInvalidToken(err.code)) {
        await authorRef.update({ fcmToken: admin.firestore.FieldValue.delete() });
      } else {
        throw err;
      }
    }

    return null;
  });

// ── Nearby note notification ──────────────────────────────────────────────────

exports.onStoryPosted = functions
  .region('europe-west2')
  .firestore.document('Stories/{storyId}')
  .onCreate(async (snap) => {
    const story = snap.data();
    if (!story) return null;

    const storyLat = story.Latitude;
    const storyLon = story.Longitude;
    const authorId = story.User;

    if (!storyLat || !storyLon || storyLat === 0 || storyLon === 0) return null;

    const db       = admin.firestore();
    const nowSecs  = Math.floor(Date.now() / 1000);
    const latDelta = NEARBY_RADIUS_KM / 111.0;
    const lonDelta = NEARBY_RADIUS_KM / (111.0 * Math.cos(storyLat * Math.PI / 180));

    const [areaName, friendsSnap, usersSnap] = await Promise.all([
      reverseGeocode(storyLat, storyLon),
      db.collection('UserProfiles').doc(authorId).collection('friends').get(),
      db.collection('UserProfiles')
        .where('lastLocation.lat', '>=', storyLat - latDelta)
        .where('lastLocation.lat', '<=', storyLat + latDelta)
        .get(),
    ]);

    // Friends get their own notification — exclude them from nearby
    const authorFriendIds = new Set(friendsSnap.docs.map(d => d.data().userId).filter(Boolean));

    if (usersSnap.empty) return null;

    const eligible = [];

    for (const doc of usersSnap.docs) {
      const user = doc.data();
      const loc  = user.lastLocation;

      if (doc.id === authorId)              continue;
      if (authorFriendIds.has(doc.id))      continue;
      if (!loc?.lat || !loc?.lon)           continue;

      const ageSecs = nowSecs - (loc.timestamp || 0);
      if (ageSecs > LOCATION_FRESH_SECS)    continue;

      const cooldownRemaining = NEARBY_COOLDOWN_SECS - (nowSecs - (user.lastNearbyNotifiedAt || 0));
      if (cooldownRemaining > 0)            continue;

      if (Math.abs(loc.lon - storyLon) > lonDelta) continue;

      const distKm = haversineKm(storyLat, storyLon, loc.lat, loc.lon);
      if (distKm > NEARBY_RADIUS_KM)        continue;

      if (!user.fcmToken)                   continue;

      eligible.push({ userId: doc.id, token: user.fcmToken });
    }

    if (eligible.length === 0) return null;

    const CHUNK = 500;
    for (let i = 0; i < eligible.length; i += CHUNK) {
      const chunk = eligible.slice(i, i + CHUNK);

      const result = await admin.messaging().sendEachForMulticast({
        tokens: chunk.map(u => u.token),
        notification: {
          title: `New notes have landed in ${areaName}`,
          body:  'Tap to explore',
        },
        data:    { type: 'story_nearby', storyId: snap.id },
        apns:    { headers: { 'apns-collapse-id': `nearby_${snap.id}` } },
        android: { collapseKey: `nearby_${snap.id}` },
      });

      const batch = db.batch();
      result.responses.forEach((resp, idx) => {
        const { userId } = chunk[idx];
        const ref = db.collection('UserProfiles').doc(userId);
        if (resp.success) {
          batch.update(ref, { lastNearbyNotifiedAt: nowSecs });
        } else if (isInvalidToken(resp.error?.code)) {
          batch.update(ref, { fcmToken: admin.firestore.FieldValue.delete() });
        }
      });
      await batch.commit();
    }

    return null;
  });

// ── Friend posted a note ──────────────────────────────────────────────────────

exports.onFriendPosted = functions
  .region('europe-west2')
  .firestore.document('Stories/{storyId}')
  .onCreate(async (snap) => {
    const story = snap.data();
    if (!story) return null;

    const authorId   = story.User;
    const authorName = story.UserName || 'Someone';
    if (!authorId) return null;

    const db = admin.firestore();

    const friendsSnap = await db.collection('UserProfiles').doc(authorId).collection('friends').get();
    if (friendsSnap.empty) return null;

    const eligible = [];

    for (const doc of friendsSnap.docs) {
      const friendUserId = doc.data().userId;
      if (!friendUserId) continue;

      const profileSnap = await db.collection('UserProfiles').doc(friendUserId).get();
      if (!profileSnap.exists) continue;

      const fcmToken = profileSnap.data().fcmToken;
      if (!fcmToken) continue;

      eligible.push({ userId: friendUserId, token: fcmToken });
    }

    if (eligible.length === 0) return null;

    const CHUNK = 500;
    for (let i = 0; i < eligible.length; i += CHUNK) {
      const chunk = eligible.slice(i, i + CHUNK);

      const result = await admin.messaging().sendEachForMulticast({
        tokens: chunk.map(u => u.token),
        notification: {
          title: `${authorName} posted a new note`,
          body:  story.Title ? `"${story.Title}"` : 'Tap to read it',
        },
        data:    { type: 'friend_posted', storyId: snap.id },
        apns:    { headers: { 'apns-collapse-id': `friend_post_${snap.id}` } },
        android: { collapseKey: `friend_post_${snap.id}` },
      });

      const batch = db.batch();
      result.responses.forEach((resp, idx) => {
        const { userId } = chunk[idx];
        if (isInvalidToken(resp.error?.code)) {
          batch.update(
            db.collection('UserProfiles').doc(userId),
            { fcmToken: admin.firestore.FieldValue.delete() }
          );
        }
      });
      await batch.commit();
    }

    return null;
  });

// ── Friend request received ───────────────────────────────────────────────────

exports.onFriendRequestReceived = functions
  .region('europe-west2')
  .firestore.document('FriendRequests/{requestId}')
  .onCreate(async (snap) => {
    const data = snap.data();
    if (!data) return null;

    const { toUserId, fromUserId, fromUsername } = data;
    if (!toUserId || !fromUsername) return null;

    const db          = admin.firestore();
    const profileSnap = await db.collection('UserProfiles').doc(toUserId).get();
    if (!profileSnap.exists) return null;

    const fcmToken = profileSnap.data().fcmToken;
    if (!fcmToken) return null;

    try {
      await admin.messaging().send({
        token: fcmToken,
        notification: {
          title: 'Friend Request',
          body:  `${fromUsername} sent you a friend request`,
        },
        data:    { type: 'friend_request', fromUserId: fromUserId || '' },
        apns:    { headers: { 'apns-collapse-id': `fr_${fromUserId}` } },
        android: { collapseKey: `fr_${fromUserId}` },
      });
    } catch (err) {
      if (isInvalidToken(err.code)) {
        await db.collection('UserProfiles').doc(toUserId)
          .update({ fcmToken: admin.firestore.FieldValue.delete() });
      } else {
        throw err;
      }
    }

    return null;
  });

// ── Friend request accepted ───────────────────────────────────────────────────
// Fires when a friend document is created in either user's subcollection.
// Both users receive "You and X are now connected."

exports.onFriendAdded = functions
  .region('europe-west2')
  .firestore.document('UserProfiles/{userId}/friends/{friendId}')
  .onCreate(async (snap, context) => {
    const { userId } = context.params;
    const friendData = snap.data();
    if (!friendData) return null;

    const friendName = friendData.username || 'Someone';

    const db          = admin.firestore();
    const profileSnap = await db.collection('UserProfiles').doc(userId).get();
    if (!profileSnap.exists) return null;

    const fcmToken = profileSnap.data().fcmToken;
    if (!fcmToken) return null;

    try {
      await admin.messaging().send({
        token: fcmToken,
        notification: {
          title: 'New Connection',
          body:  `You and ${friendName} are now connected`,
        },
        data:    { type: 'friend_accepted', friendId: friendData.userId || '' },
        apns:    { headers: { 'apns-collapse-id': `fa_${friendData.userId}` } },
        android: { collapseKey: `fa_${friendData.userId}` },
      });
    } catch (err) {
      if (isInvalidToken(err.code)) {
        await db.collection('UserProfiles').doc(userId)
          .update({ fcmToken: admin.firestore.FieldValue.delete() });
      } else {
        throw err;
      }
    }

    return null;
  });

// ── Helpers ───────────────────────────────────────────────────────────────────

function haversineKm(lat1, lon1, lat2, lon2) {
  const R    = 6371;
  const dLat = (lat2 - lat1) * Math.PI / 180;
  const dLon = (lon2 - lon1) * Math.PI / 180;
  const a    = Math.sin(dLat / 2) ** 2
             + Math.cos(lat1 * Math.PI / 180)
             * Math.cos(lat2 * Math.PI / 180)
             * Math.sin(dLon / 2) ** 2;
  return R * 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a));
}

function isInvalidToken(code) {
  return code === 'messaging/registration-token-not-registered' ||
         code === 'messaging/invalid-registration-token';
}

function reverseGeocode(lat, lon) {
  return new Promise((resolve) => {
    const options = {
      hostname: 'nominatim.openstreetmap.org',
      path:     `/reverse?lat=${lat}&lon=${lon}&format=json`,
      headers:  { 'User-Agent': 'InkJourney/1.0' },
    };
    https.get(options, (res) => {
      let raw = '';
      res.on('data', chunk => raw += chunk);
      res.on('end', () => {
        try {
          const addr = JSON.parse(raw).address || {};
          resolve(
            addr.suburb       ||
            addr.town         ||
            addr.village      ||
            addr.city_district||
            addr.city         ||
            addr.county       ||
            'your area'
          );
        } catch { resolve('your area'); }
      });
    }).on('error', () => resolve('your area'));
  });
}
