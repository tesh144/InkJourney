const admin = require('firebase-admin');
const serviceAccount = require('./serviceAccountKey.json'); // download from Firebase Console → Project Settings → Service Accounts

admin.initializeApp({
    credential: admin.credential.cert(serviceAccount),
    projectId: 'inkjourney'
});

const db = admin.firestore();

const TAG_RENAMES = {
    'historical':  'history',
    'hidden_gems': 'secret_spots',
    'buildings':   'architecture',
    'creative':    'creative_writing',
    'wildlife':    'nature',
};

const REMOVED_TAGS = new Set([
    'wholesome', 'culture', 'exploration', 'magical',
    'poetry', 'mysteries', 'religion', 'landmark'
]);

function migrateTags(tags) {
    if (!Array.isArray(tags)) return null;

    const updated = tags
        .filter(t => !REMOVED_TAGS.has(t))
        .map(t => TAG_RENAMES[t] ?? t);

    const changed = JSON.stringify(tags) !== JSON.stringify(updated);
    return { updated, changed };
}

async function migrateCollection(name) {
    const snapshot = await db.collection(name).get();
    let updated = 0;

    for (const doc of snapshot.docs) {
        const data = doc.data();
        const result = migrateTags(data.Tags);

        if (!result || !result.changed) continue;

        console.log(`[${name}] ${doc.id}: ${JSON.stringify(data.Tags)} → ${JSON.stringify(result.updated)}`);
        await doc.ref.update({ Tags: result.updated });
        updated++;
    }

    console.log(`[${name}] Done — ${updated}/${snapshot.size} documents updated.`);
}

(async () => {
    await migrateCollection('Stories');
    await migrateCollection('Landmarks');
    console.log('Migration complete.');
    process.exit(0);
})();
