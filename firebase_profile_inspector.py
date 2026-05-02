import argparse
import json
import os
import sys
from typing import Optional

try:
    import firebase_admin
    from firebase_admin import credentials, firestore
except ImportError:
    print("Missing firebase_admin. Install with: pip install firebase-admin")
    sys.exit(1)


def init_firestore(service_account_json: Optional[str], project_id: Optional[str]):
    if service_account_json:
        cred = credentials.Certificate(service_account_json)
        firebase_admin.initialize_app(cred, {'projectId': project_id} if project_id else None)
    else:
        firebase_admin.initialize_app()
    return firestore.client()


def find_user_docs(db, collection_name: str, owner_id: Optional[str], owner_name: Optional[str]):
    query = db.collection(collection_name)

    filters = []
    if owner_id:
        filters.append(('User', '==', owner_id))
    if owner_name:
        filters.append(('UserName', '==', owner_name))
        filters.append(('User', '==', owner_name))

    if not filters:
        print('No owner_id or owner_name specified. Nothing to search.')
        return []

    results = []
    seen_ids = set()

    for field, op, value in filters:
        docs = query.where(field, op, value).stream()
        for doc in docs:
            if doc.id in seen_ids:
                continue
            seen_ids.add(doc.id)
            results.append((doc.id, doc.to_dict()))

    return results


def normalize_doc(doc_data, owner_id: str, owner_name: str, current_username: str):
    updated = False
    data = doc_data.copy()

    if 'User' not in data or not data.get('User'):
        data['User'] = owner_id
        updated = True

    if data.get('User') != owner_id:
        # keep the stored user id, but only if it appears to be legacy username
        if data.get('User') in {owner_name, current_username, 'You', 'USERNAME'}:
            data['User'] = owner_id
            updated = True

    if data.get('UserName') != current_username:
        data['UserName'] = current_username
        updated = True

    return updated, data


def pretty_print_entry(doc_id: str, entry: dict):
    print('---')
    print(f'Document ID: {doc_id}')
    for key in sorted(entry.keys()):
        print(f'{key}: {entry[key]}')


def main():
    parser = argparse.ArgumentParser(description='Inspect Firebase Firestore story ownership for a user.')
    parser.add_argument('--service-account', help='Path to Firebase service account JSON file.')
    parser.add_argument('--project-id', help='Firebase project ID. Optional if service account contains it.')
    parser.add_argument('--collection', default='Stories', help='Firestore collection name (default: Stories).')
    parser.add_argument('--owner-id', help='Current user id to identify story ownership.')
    parser.add_argument('--owner-name', help='Current username to identify story ownership.')
    parser.add_argument('--current-username', help='Current local username for normalization checks.')
    parser.add_argument('--dump-all', action='store_true', help='Dump every matching document.')
    parser.add_argument('--fix', action='store_true', help='Update matching documents to normalize User/UserName values.')
    args = parser.parse_args()

    if not args.owner_id and not args.owner_name:
        print('You must provide --owner-id or --owner-name (or both).')
        parser.print_help()
        sys.exit(1)

    db = init_firestore(args.service_account, args.project_id)
    entries = find_user_docs(db, args.collection, args.owner_id, args.owner_name)

    if not entries:
        print('No matching documents found.')
        return

    print(f'Found {len(entries)} matching documents in collection {args.collection}.')

    for doc_id, entry in entries:
        pretty_print_entry(doc_id, entry)

        if args.fix:
            if not args.owner_id or not args.current_username:
                print('Skipping fix because --owner-id and --current-username are required for normalization.')
                continue

            updated, normalized = normalize_doc(entry, args.owner_id, args.owner_name or args.current_username, args.current_username)
            if updated:
                print(f'Updating document {doc_id} to normalized ownership values...')
                db.collection(args.collection).document(doc_id).set(normalized, merge=True)
            else:
                print(f'Document {doc_id} already normalized.')

    if args.fix:
        print('Fix complete.')


if __name__ == '__main__':
    main()
