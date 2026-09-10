from pathlib import Path
import copy, json, sys, unittest
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from validate import read, validate_catalog, schema_check, ValidationError, inside

ROOT=Path(__file__).resolve().parents[1]
class ImportGuardTests(unittest.TestCase):
    def setUp(self):
        self.heroes=read(ROOT/'content/canonical/heroes.json')['heroes']
        self.cards=read(ROOT/'content/canonical/cards.json')['cards']
        self.board=read(ROOT/'content/canonical/map.json')['cells']
        self.status=read(ROOT/'content/status/cards.json')['cards']
    def check(self):
        validate_catalog(self.heroes,self.cards,self.board,self.status)
    def test_duplicate_card_id_rejected(self):
        self.cards[1]['id']=self.cards[0]['id']
        with self.assertRaises(ValidationError):self.check()
    def test_missing_upgrade_candidate_rejected(self):
        self.cards[2]['level']=1
        with self.assertRaises(ValidationError):self.check()
    def test_broken_hero_reference_rejected(self):
        self.cards[0]['hero_id']='unknown'
        with self.assertRaises(ValidationError):self.check()
    def test_zero_movement_cannot_be_active(self):
        self.cards[0]['secondary_actions']['movement']={'type':'移动','has_action':True,'value':0}
        with self.assertRaises(ValidationError):self.check()
    def test_duplicate_coordinate_rejected(self):
        self.board[1]['x']=self.board[0]['x']; self.board[1]['y']=self.board[0]['y']
        with self.assertRaises(ValidationError):self.check()
    def test_wrong_spawn_distribution_rejected(self):
        cell=next(c for c in self.board if c['state']=='redMeleeSpawn' and c['region']=='mid')
        cell['state']='blueMeleeSpawn'
        with self.assertRaises(ValidationError):self.check()
    def test_inflated_card_status_rejected(self):
        # Keep the missing-evidence fixture independent of which cards are implemented.
        self.status[0].update(status='integration_tested',contract=None,tests=[],implementation_files=[])
        with self.assertRaises(ValidationError):self.check()
    def test_schema_rejects_boolean_as_integer(self):
        schema=read(ROOT/'content/schemas/cards.schema.json')
        self.cards[0]['initiative']=True
        with self.assertRaises(ValidationError):schema_check({'schema_version':'1.0.0','cards':self.cards},schema)
    def test_duplicate_json_key_rejected(self):
        from validate import unique_object
        with self.assertRaises(ValidationError):json.loads('{"id":1,"id":2}',object_pairs_hook=unique_object)
    def test_source_path_traversal_rejected(self):
        with self.assertRaises(ValidationError):inside(ROOT,'../goa2_v4/data/cards.json')
    def test_valid_baseline_and_literal_legacy_ids(self):
        self.check()
        self.assertTrue(any(c['id'].startswith('tigerclaw-18-') for c in self.cards))
if __name__=='__main__':unittest.main()
